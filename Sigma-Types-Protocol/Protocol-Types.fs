namespace Sigma.Types

    [<AutoOpen>]
    module Protocol =

        open System

        (** Used by clients to understand which of (potentially) many microservices (of a given type) has best availability **)
        type QueueRequest = {
            jobID      : string
            jobType    : string
            apiVersion : string
            priority   : int
        } 

        (** Sent in response to an QueueRequest **)
        type QueueResponse = {
            jobID         : string
            channel       : string
            queuePosition : int
            expires       : DateTime
        }

        (** Response to a job request **)
        type JobSubmissionResponse = {
            jobID    : string
            accepted : bool
            error    : string
            channel  : string
        }

        (** Submitted when a client needs to check the status of a job **)
        type QueueStatusRequest = {
            jobID: string
        }

        (** Response to a QueueStatusRequest **)
        type QueueStatusResponse = {
            jobID         : string
            processed     : bool
            queuePosition : int
        }

        (** Job Stage **)
        type JobStage = 
            | Preprocessing
            | Processing
        
        (** Protocol Error **)
        type ProtocolError = 
            | No_Error
            | No_Audition_Responses
            | No_Submission_Confirmation
            | Job_Rejected
            | Queue_Advance_Threshold_Exceeded
            | Response_Failure_Threshold_Exceeded

        (** Job Status **)
        type JobStatusCode = 
            | Unknowns
            | Pending
            | Submitted
            | Processing of int 
            | Protocol_Failure of ProtocolError list
            | Failed
            | Completed 

        (** Job Status **)
        type JobStatus = {
            stage: JobStage
            code : JobStatusCode
        }

        (** Response container **)
        type ProtocolResponse<'T> = {
            response: 'T option
            error: ProtocolError
        } 

        (** Log entry **)
        type LogType = 
           | Error   of string
           | Warning of string
           | Info    of string
           | Success of string

        (** Log entries, which are streamed over RabbitMQ **)
        type LogEntry = {
            jobID : String
            entry : LogType 
        }