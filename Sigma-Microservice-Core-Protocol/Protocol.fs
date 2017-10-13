module Sigma.Common.Protocols.Microservices

open System
open System.IO
open System.Text
open System.Linq
open System.Collections.Generic
open RabbitMQ.Client
open RabbitMQ.Client.Events
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open Sigma.Common.RabbitMQ
open Sigma.Types
open Sigma.Types.API
open Sigma.Lib
open StatsdClient

(** Work queue type **)
type QueueItem = { guid: string;  priority: int;  request: string option; requestRecieved: DateTime }

(** Union type for managing access to the shared queue **)
type QueueModification = 
| QueueItemRemove of QueueItem
| QueueSetRemove  of QueueItem list
| QueueItemAdd    of QueueItem

(** Provides a core microservices protocol **)
[<AbstractClass>]
type MicroserviceCoreProtocol<'T when 'T :> IJobRequestInterface> (serviceGuid: Guid, jobType: string, rabbit: ConnectionFactory, sleepInterval: int) = 
    
    (** Work is stored on an inbound queue and moved to the outbound queue when complete **)
    let inboundQueue = ref List.empty<QueueItem>
    let outboundQueue = ref List.empty<QueueItem>

    (** Rabbit MQ connection **)
    let rmqConn = rabbit.CreateConnection()

    (** Lock: make sure that two threads can't update the inbound queue at once **)
    let inboundQueueLock = new Object()
    
    (** Logging **)
    static member log (message: string) = message |> Console.WriteLine
    static member dataDogEvent (alertType: AlertType) message title = 
        DogStatsd.Event(title, message, DiscriminatedUnion.toString alertType)
        String.Format("{0}: {1}: {2}", DiscriminatedUnion.toString alertType, title, message)

    (* Longest time out we can store in an Int32 *)
    static member LONG_TIMEOUT = 31540000
   
    (** Unique identifier for this service **)
    member x.serviceGuid    = serviceGuid
    member x.serviceGuidStr = serviceGuid.ToString()
    
    (** How log will the service wait, in seconds, to get a valid job request, in part two of the protocol **)
    member x.requestValidWindow = new TimeSpan(0, 0, 120)

    (** Standard sleep interval **)
    member x.sleepInterval = sleepInterval

    (** The name of the service, e.g. SIGMA_WORKER, SIGMA_INTERPRETER, GOOGLE_ANALYTCS, etc **)
    member x.rootServiceName     = jobType
    member x.rootServiceNameGuid = x.rootServiceName + "_" + x.serviceGuidStr 

    (** DataDog event helpers **)
    member x.statsIncrement (elements: string list, ?inc: int) = 
        let defInc = defaultArg inc 1
        DogStatsd.Increment(elements.toDotSeparated(), defInc)

    (** Exchanges **)
    member x.auditionExchange         = x.rootServiceName  + "_Audition_Request"
    member x.JobRequestExchange       = x.rootServiceName  + "_Job_Request"             + "_" + x.serviceGuidStr
    member x.JobStatusRequestExchange = x.rootServiceName  + "_Job_Status_Request"      + "_" + x.serviceGuidStr

    (** Queues **)
    member x.auditionQueue            = x.auditionExchange         + "_" + x.serviceGuidStr + "_Queue"
    member x.JobRequestQueue          = x.JobRequestExchange       +                          "_Queue"
    member x.JobStatusRequestQueue    = x.JobStatusRequestExchange +                          "_Queue"

    (** Shortcut for creating a thread thread that listens for a particular Rabbit MQ routing key and feeds messages to a callback **) 
    member x.genericListen callback exchange queue = simpleListen queue exchange ExchangeType.Fanout callback rabbit MicroserviceCoreProtocol<_>.LONG_TIMEOUT (ref false) (ref false)
    
    (** Get the position of a particular job id in the inbound queue, after sorting by priority **)
    member x.getQueuePosition jobId = 
        inboundQueue.contents
            .OrderByDescending(fun x -> x.priority)
                .ToList()
                    .FindIndex(fun x -> x.guid = jobId)
 
    (** Update inboundQueue **)
    member x.updateInboundQueue (edit: QueueModification) = 
        lock inboundQueueLock (fun () -> 
            match edit with 
            | QueueItemRemove remove -> inboundQueue := inboundQueue.contents.Except( remove  :: List.empty<QueueItem> ) |> List.ofSeq
            | QueueSetRemove  remove -> inboundQueue := inboundQueue.contents.Except( remove ) |> List.ofSeq
            | QueueItemAdd    add    -> inboundQueue := add :: !inboundQueue
        )
    
    (** CALLBACK - triggered when a QueueRequest is recieved **)
    member x.audition (args: BasicDeliverEventArgs) = 
        try 
            x.statsIncrement [ "queuerequest" ; "received"]
            let queueRequest = JsonConvert.DeserializeObject<QueueRequest>(Encoding.ASCII.GetString(args.Body))
            String.Format("{1}: Got QueueRequest for {0}", queueRequest.jobID.ToString(), DateTime.Now.ToLongTimeString()) |> MicroserviceCoreProtocol<_>.log
            { QueueItem.guid = queueRequest.jobID; priority = queueRequest.priority; request = None; requestRecieved = DateTime.Now} |> QueueItemAdd |> x.updateInboundQueue 
            { jobID = queueRequest.jobID; channel = x.JobRequestExchange ; queuePosition = x.getQueuePosition queueRequest.jobID ; expires = (DateTime.Now + x.requestValidWindow) }
            |> JsonConvert.SerializeObject |> connectAndPost "" (Some args.BasicProperties.ReplyTo) rabbit None None
            String.Format("{1}: Sent QueueResponse for {0}", queueRequest.jobID.ToString(), DateTime.Now.ToLongTimeString()) |> MicroserviceCoreProtocol<_>.log 
            x.statsIncrement [ "queueresponse" ; "sent"]
        with | ex -> String.Format("Exception handling QueueRequest for {0}", x.rootServiceNameGuid) |> MicroserviceCoreProtocol<_>.dataDogEvent AlertType.Error ex.Message |> MicroserviceCoreProtocol<_>.log
    
    (** CALLBACK - triggered when a Job is recieved **)
    member x.JobRequest (args: BasicDeliverEventArgs) = 
        try
            x.statsIncrement [ "jobrequest" ; "received"]
            let requestAsString = Encoding.ASCII.GetString(args.Body)
            let myRequest = JsonConvert.DeserializeObject<'T>(requestAsString)
            String.Format("{1}: Got Job with ID {0}", myRequest.jobID.ToString(), DateTime.Now.ToLongTimeString()) |> MicroserviceCoreProtocol<_>.log |> ignore 
            let matching = inboundQueue.contents.Where(fun x -> x.guid = myRequest.jobID)
            ( match matching.Count() with 
              | 0 -> 
                    x.statsIncrement [ "jobrequest" ; "rejected" ]
                    String.Format("{0}: No matching job id (it might have been pruned, or not recieved)", DateTime.Now.ToLongTimeString()) |> MicroserviceCoreProtocol<_>.log 
                    { jobID = myRequest.jobID; accepted = false; error = "No matching job id (it might have been pruned, or not recieved)"; channel = ""}
              | _ -> 
                    let toUpdate = matching.First()
                    toUpdate |> QueueItemRemove |> x.updateInboundQueue
                    String.Format("{1}: Adding Job ID {0} to queue", myRequest.jobID.ToString(), DateTime.Now.ToLongTimeString()) |> MicroserviceCoreProtocol<_>.log 
                    { guid = toUpdate.guid; priority = toUpdate.priority; request = Some requestAsString; requestRecieved = DateTime.Now} |> QueueItemAdd |> x.updateInboundQueue
                    { jobID = myRequest.jobID; accepted = true; error = ""; channel = x.JobStatusRequestExchange } 
            ) |> JsonConvert.SerializeObject |> connectAndPost "" (Some args.BasicProperties.ReplyTo) rabbit None None
            x.statsIncrement [ "jobsubmissionresponse" ; "sent" ]
        with | ex -> String.Format("Exception handling JobRequest for {0}", x.rootServiceNameGuid) |> MicroserviceCoreProtocol<_>.dataDogEvent AlertType.Error ex.Message |> MicroserviceCoreProtocol<_>.log

    (** CALLBACK - triggered when a QueueStatusRequest is recieved **)
    member x.jobStatusRequest (args: BasicDeliverEventArgs) =
        try
            x.statsIncrement [ "queuestatusrequest" ; "received" ]
            let queueStatusRequest = JsonConvert.DeserializeObject<QueueStatusRequest>(Encoding.ASCII.GetString(args.Body))
            let isProcessed = outboundQueue.contents.Where(fun x -> x.guid = queueStatusRequest.jobID).Count() > 0
            let queuePosition = match isProcessed with | true -> 0 | false -> x.getQueuePosition queueStatusRequest.jobID
            String.Format("{2}: Got Job Status Request for Job ID {0} (Responding with position {1})", queueStatusRequest.jobID.ToString(), queuePosition, DateTime.Now.ToLongTimeString()) |> MicroserviceCoreProtocol<_>.log 
            { jobID = queueStatusRequest.jobID; processed = isProcessed; queuePosition = queuePosition }       
            |> JsonConvert.SerializeObject |> connectAndPost "" (Some args.BasicProperties.ReplyTo) rabbit None None
            x.statsIncrement [ "queuestatusresponse" ; "sent" ]
        with | ex -> String.Format("Exception handling jobStatusRequest for {0}", x.rootServiceNameGuid) |> MicroserviceCoreProtocol<_>.dataDogEvent AlertType.Error ex.Message |> MicroserviceCoreProtocol<_>.log

    (** Removes jobs from the inbound queue if the client doesn't follow up their QueueRequest with a valid Job request within the requestValidWindow **)
    member x.pruneQueue sleepInterval = async {
        let timedOut = inboundQueue.contents.Where(fun item -> item.request.IsNone && (item.requestRecieved + x.requestValidWindow) < DateTime.Now)
        match timedOut.Count() with 
        | 0 -> () 
        | _ -> x.statsIncrement([ "pruningjob" ], timedOut.Count())
               let toPruneConsole = timedOut.Select(fun x -> x.guid.ToString()) |> List.ofSeq |> List.reduce (fun state item -> state + ", " + item)
               String.Format("{1}: Pruning due to non-response: {0}", toPruneConsole, DateTime.Now.ToLongTimeString()) |> MicroserviceCoreProtocol<_>.log 
               timedOut |> List.ofSeq |> QueueSetRemove |> x.updateInboundQueue
        do! Async.Sleep(sleepInterval)
        return! x.pruneQueue sleepInterval
    }

    (** Triggered when a job is complete **)    
    member x.moveJobToOutbox (myQueueItem: QueueItem) = 
        myQueueItem |> QueueItemRemove |> x.updateInboundQueue
        outboundQueue := myQueueItem :: !outboundQueue

    (** Function provided in constructor - does the actual work specific to a microservice intance **)
    abstract member processJob: 'T -> unit

    (** Triggered for each queue item **)    
    member x.processQueueItem (myQueueItem: QueueItem) = 
        try 
            x.statsIncrement([ "processjob" ], 1)
            JsonConvert.DeserializeObject<'T>(myQueueItem.request.Value) |> x.processJob
            x.moveJobToOutbox myQueueItem
        with | ex -> String.Format("Exception handling processQueueItem for {0}", x.rootServiceNameGuid) |> MicroserviceCoreProtocol<_>.dataDogEvent AlertType.Error ex.Message |> MicroserviceCoreProtocol<_>.log

    (** Checks for uncomming work, processes it, then sleep, repeat **)  
    member x.processQueue (sleepInterval: int) = async {        
        inboundQueue.contents.Where(fun x -> x.request.IsSome)
            .OrderByDescending(fun x-> x.priority) 
                |> Seq.iter x.processQueueItem 
        do! Async.Sleep sleepInterval
        return! x.processQueue sleepInterval
    }

    (** Heartbeat **)
    member x.heartbeat freq = 
        
         (** Loop, sending a heartbeat every 'freq' seconds **)
        let rec doHeartbeat () = async {
            
            (** Sleep **)
            do! Async.Sleep ( freq * 1000 )

            try
                (** Get the schedule hash, create MasterScheduleConsensusCheck record, and broadcast it to the scheduleConsensusChannel **)
                using (rmqConn.CreateModel()) ( fun channel -> { MicroserviceHeartbeat.serviceId = x.serviceGuid ; serviceType = jobType.Replace("_", " ") ; stamp = DateTime.Now }
                                                                 |> JsonConvert.SerializeObject |> post "Microservice-Heartbeats" None rmqConn channel None None 
                )
            with | ex -> String.Format("Exception sending heartbeat for {0}", x.rootServiceNameGuid) |> MicroserviceCoreProtocol<_>.log

            (** Loop **)
            return! doHeartbeat ()
        }

        (** Go! **)
        doHeartbeat ()

    (** Instrumentation  **)
    member x.startInstrumentation () = 
        let dataDogConfig = new StatsdConfig()
        dataDogConfig.Prefix <- String.Format("microservice.{0}", x.rootServiceName.Replace("_", "").ToLower())
        dataDogConfig.StatsdServerName <- "127.0.0.1"
        dataDogConfig.StatsdPort <- 8125
        StatsdClient.DogStatsd.Configure(dataDogConfig);

    (** Start the service **)
    member x.startService sleepInterval = 
        
        (** Start DataDog capture **)
        x.startInstrumentation() |> ignore
        
        (** Listeners **)
        async { x.genericListen x.audition         x.auditionExchange         x.auditionQueue         } |> Async.Start
        async { x.genericListen x.JobRequest       x.JobRequestExchange       x.JobRequestQueue       } |> Async.Start
        async { x.genericListen x.jobStatusRequest x.JobStatusRequestExchange x.JobStatusRequestQueue } |> Async.Start
        
        (** background threads **)
        x.pruneQueue   sleepInterval |> Async.Start
        x.processQueue sleepInterval |> Async.Start  
        x.heartbeat    10            |> Async.Start 
        
        (** Start up event **)
        DogStatsd.Event("Microservice: " + x.rootServiceName + " startup sucessful", "", "success")  