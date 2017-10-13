module Sigma.APIEngine.Microservices

open System
open Sigma.Common.RabbitMQ
open Sigma.APIs.Connectors
open Newtonsoft.Json
open RiakClient
open RiakClient.Models
open Sigma.Lib
open Sigma.Types
open Sigma.Types.API
open Sigma.APIEngine.Microservices


(** Riak **)
let riakCluster = RiakCluster.FromConfig("riakConfig")
let riakClient = riakCluster.CreateClient()

(** Credentials Riak bucket **)
let credentialsRiakBucket = "AmazonAdvertising-Refresh-Tokens"

(** Create a guid for this service, which will be used as a routing key **)
let SERVICE_GUID = Guid.NewGuid() 

(** Create our connection factory **)
let rabbit = createConnectionFactory  "172.21.24.37"
rabbit.Password <- "password" 
rabbit.UserName <-"admin" 
rabbit.RequestedHeartbeat <- UInt16.Parse("60")

//let clientId = ""
//let clientSecret = ""
//let savedRefreshToken = ""

(** Automatic recovery for RabbitMQ **)
rabbit.AutomaticRecoveryEnabled <- true

(** Plugin for handling jobs **)
let processJob (requestContainer: AmazonAdvertisingRequest)  = 
    
    (** So that we can get JobType, etc **)
    let asRequestInterface = requestContainer :> IJobRequestInterface

    (** Template response **)
    let responseTemplate = { APIMicroserviceResponse.jobID = requestContainer.jobGuid; jobType = asRequestInterface.jobType; request = JsonConvert.SerializeObject( requestContainer ) ; resultKeys = None ;  success = false ; error = ""; retryable = false ; isCacheHit = false ; processed = DateTime.Now }
    
    try 
        
        (** Fn to store results in Riak **)
        let handleResponse (response: AmazonAPIResponseWrapper<'T>) = 
            response
            |> function | Results toStore -> let resultKey = String.Format("{0}-0", asRequestInterface.jobID.ToString())
                                             match RiakHelper.Insert(riakClient, asRequestInterface.jobType, resultKey, toStore) with 
                                             | x when x.ResultCode = ResultCode.Success -> { responseTemplate with resultKeys = resultKey :: List.empty<string> |> Some ; success = true }  
                                             | _ -> { responseTemplate with error = AmazonAdvertisingError.StorageError |> JsonConvert.SerializeObject ; retryable = true }    
                        | AmazonAdvertisingError err -> match err with 
                                                         | AmazonAdvertisingAPIError x          -> false
                                                         | WebRequestError x                    -> false
                                                         | GeneralError    x                    -> false
                                                         | NoAPIKey                             -> false
                                                         | StorageError                         -> false
                                                         |> fun retry -> { responseTemplate with error = err |> JsonConvert.SerializeObject ; retryable = retry}   
                        
        (** Look up credentials key in Riak **)
        let credentials = RiakHelper.GetValue<Credentials> riakClient credentialsRiakBucket requestContainer.amazonAdvertisingUserName

        (** GetAppToken doesn't require any credentials, all other calls do **)
        match requestContainer.request with 
        | GetAWSAppAccessTokenUsingRefreshToken clientId -> AmazonAdvertising.GetAWSAppAccessTokenUsingRefreshToken credentials.Value.RefreshToken clientId credentials.Value.ClientSecret  |> handleResponse
        | _                     -> match credentials.IsSome with
                                   | false -> { responseTemplate with error = AmazonAdvertisingError.NoAPIKey |> JsonConvert.SerializeObject } 
                                   | true  -> match requestContainer.request with 
                                              | CreateProfile countryCode -> AmazonAdvertising.CreateProfile credentials.Value.AccessToken countryCode |> handleResponse
                                              | GetProfile profileId -> AmazonAdvertising.GetProfile credentials.Value.AccessToken profileId |> handleResponse
                                              | GetCampaign campaignRequest -> AmazonAdvertising.GetCampaigns credentials.Value.AccessToken campaignRequest.campaignId campaignRequest.profileId |> handleResponse
                                              | AmazonReportRequest reportRequest -> AmazonAdvertising.CreateReport credentials.Value.AccessToken reportRequest.profileId reportRequest.reportType reportRequest.reportRequest |> handleResponse
                                              | AmazonReportById getReportRequest -> AmazonAdvertising.GetReportById credentials.Value.AccessToken getReportRequest.profileId getReportRequest.reportId  |> handleResponse
    with | ex -> { responseTemplate with error = ex.Message |> JsonConvert.SerializeObject } 

(** Create service **)
let myMicroservice = new APIEngineMicroservice<AmazonAdvertisingRequest>(new TimeSpan(0, 0, 10, 0), 30, riakCluster, rabbit, "AWS", processJob)

(** Create our Microservice **)
Console.WriteLine("Starting Amazon API Microservice ...")

(** Start service **)
myMicroservice.startService 1000

(** Wait forever **)
while true do System.Threading.Thread.Sleep (Int32.MaxValue)