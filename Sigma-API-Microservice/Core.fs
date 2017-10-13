module Sigma.APIEngine.Microservices

open System
open System.Text
open System.Threading
open System.Linq
open System.Collections.Generic
open System.Text.RegularExpressions
open Sigma.Common.RabbitMQ
open Sigma.Types
open Sigma.Types.API
open Sigma.Lib
open Newtonsoft.Json
open RiakClient
open RiakClient.Models
open RabbitMQ.Client
open RabbitMQ.Client.Events
open Sigma.Common.Protocols.Microservices
open StatsdClient

(** Work queue type **)
type queueItem = { guid: Guid;  priority: int;  request: string option; requestRecieved: DateTime }

(** Riak bucket names **)
let RATE_LIMIT_DATA = "Api-Engine-Rate-Limiting"

(** GUID handling **)
let Guidregex = @"\b[A-F0-9]{8}(?:-[A-F0-9]{4}){3}-[A-F0-9]{12}\b"
let stripGuids inString = Regex.Replace(inString, Guidregex, "", RegexOptions.IgnoreCase)

(** Generic API Microservice - instantiate with the type of the request that the service will handle  **)
type APIEngineMicroservice<'T when 'T :> IJobRequestInterface> (rateLimitTimeHorizon: TimeSpan, rateLimit: int, riakEndPoint: IRiakEndPoint, rabbit: ConnectionFactory, jobType: string, processAPICall: 'T -> APIMicroserviceResponse)  = 
    inherit MicroserviceCoreProtocol<'T>(Guid.NewGuid(), jobType, rabbit, 1000)
        
        (** Riak **)
        member x.riakEndPoint = riakEndPoint
        member x.riak = riakEndPoint.CreateClient()

        (** Processes a job (via x.processJob) using the cache if possible, if a cacheLimit is provided **)
        member x.processViaCache (myRequest: 'T) = 
            x.statsIncrement [ "processingjob" ]
            let asRequestInterface = myRequest :> IJobRequestInterface
            let cacheLimit = try asRequestInterface.cacheLimit with | ex -> None
            let result = 
                match cacheLimit.IsSome with 
                | true -> 
                    let requestAsString = JsonConvert.SerializeObject(myRequest)
                    let requestHash =  requestAsString |> stripGuids |> Encoding.ASCII.GetBytes|> md5 
                    let indexId = new RiakIndexId(myRequest.jobType, "RequestHash")
                    let secondaryIndex = x.riak.GetSecondaryIndex(indexId, requestHash)
                    let matchingRecords = secondaryIndex.Value.IndexKeyTerms 
                                          |> Seq.map (fun y -> x.riak.Get(myRequest.jobType, y.Key).Value.GetObject<APIMicroserviceResponse>())
                                          |> Seq.filter (fun x -> x.processed > DateTime.Now.Subtract(cacheLimit.Value))
                    match matchingRecords.Count() with 
                    | 0 -> processAPICall myRequest
                    | _ -> x.statsIncrement [ "cachehit" ]
                           String.Format("{1}: Cache hit for job ID {0}", asRequestInterface.jobID.ToString(), DateTime.Now.ToLongTimeString()) |> MicroserviceCoreProtocol<_>.log 
                           let latestResponse = matchingRecords.OrderByDescending(fun x -> x.processed).First()
                           { jobID = Guid.Parse (myRequest.jobID) ; jobType = myRequest.jobType ; request = JsonConvert.SerializeObject( myRequest ) ; resultKeys = latestResponse.resultKeys; success = latestResponse.success; error = latestResponse.error; retryable = latestResponse.retryable ; isCacheHit = true ; processed = latestResponse.processed }
                | false -> processAPICall myRequest 
            result, myRequest

        (** Processes request within the rate limit, then sleep for an interval **)
        member x.rateLimiter (myRequest: 'T) = 
            let asRequestInterface = myRequest :> IJobRequestInterface

            let rec bePolite (sleepInterval: int) = 
                let indexId = new RiakIndexId(RATE_LIMIT_DATA, "JobType")
                let secondaryIndex = x.riak.GetSecondaryIndex(indexId, asRequestInterface.jobType)
                let keys = secondaryIndex.Value.IndexKeyTerms |> Seq.map (fun y -> x.riak.Get(RATE_LIMIT_DATA, y.Key)) 
                let records = keys |> Seq.map(fun x -> x.Value.GetObject<RateLimitRecord>()) 
                let removeOlderThan = DateTime.Now.Subtract(rateLimitTimeHorizon)
                let toPrune = keys |> Seq.filter (fun x -> x.Value.GetObject<RateLimitRecord>().processed < removeOlderThan)
                let currentRate = keys.Count() - toPrune.Count()
                let allowed = rateLimit - currentRate
                String.Format("{1}: Current rate capacity = {0}", allowed, DateTime.Now.ToLongTimeString()) |> MicroserviceCoreProtocol<_>.log 
            
                // Remove records older than our time horizon
                toPrune |> Seq.iter(fun y -> x.riak.Delete(y.Value) |> ignore)
            
                // Do request now, or wait a little
                match allowed >= 1 with 
                | true  -> myRequest 
                | false -> x.statsIncrement [ "ratelimiting" ]
                           String.Format("Rate limiting!") |> MicroserviceCoreProtocol<_>.log 
                           Thread.Sleep sleepInterval 
                           bePolite sleepInterval

            bePolite x.sleepInterval
    
        (** Keeps a record of calls made **)    
        member x.addRateLimitRecord (jobResponse: APIMicroserviceResponse) =
            match jobResponse.isCacheHit with 
            | false ->  
                let rateLimitRecord = { RateLimitRecord.processed = jobResponse.processed }
                new RiakObject(RATE_LIMIT_DATA, jobResponse.jobID.ToString(), JsonConvert.SerializeObject( rateLimitRecord )) 
                |> RiakHelper.AddRiakSecondaryIndex "JobType" (jobResponse.jobType) true |> x.riak.Put |> ignore
            | true -> ()

        (** Sends the results of a sucessfully processed job to Riak **) 
        member x.toDatastore (response: APIMicroserviceResponse, myRequest: 'T) =    
            let asRequestInterface = myRequest :> IJobRequestInterface
            let requestHash = response.request |> stripGuids |> Encoding.ASCII.GetBytes|> md5
            new RiakObject(response.jobType, asRequestInterface.jobID.ToString(), response) 
                |> fun partial -> ( match response.success with 
                                    | true  -> x.addRateLimitRecord response 
                                               partial |> RiakHelper.AddRiakSecondaryIndex "RequestHash" requestHash true
                                    | false -> partial   
                                  ) |> x.riak.Put |> ignore
        
        override x.processJob (job: 'T) = job |> x.rateLimiter |> x.processViaCache |> x.toDatastore 