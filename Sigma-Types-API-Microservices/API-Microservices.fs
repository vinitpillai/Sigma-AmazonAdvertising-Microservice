namespace Sigma.Types.API

    [<AutoOpen>]
    module Microservices =

        open System
        open Sigma.Types.Protocol

        (** Used to record data used for API rate limiting **)
        type RateLimitRecord = {
            processed: DateTime
        }
        
        (** Retryable error **)
        type RetryableError = 
        | Retryable
        | NotRetryable

        (** Error handling for Microservices **)
        type MicroserviceError<'E> = 
        | ProtocolError of ProtocolError (** Error at the protocol level **)
        | StorageError                   (** Error fetching data from storage  **)
        | ProcessingError of 'E          (** Error from with the scope of the microservice **)

        (** Microservice result; just a Riak key collection **)
        type MicroserviceResultKeys<'E> =
        | Success of string list
        | Failure of MicroserviceError<'E> * RetryableError

        (** Microservice Result; with keys deserialised from Riak **)
        type MicroserviceResultList<'S, 'E> =
        | Success of 'S list
        | Failure of MicroserviceError<'E> * RetryableError

         (** Microservice Result; with keys deserialised from Riak and reduced to a single object using some function **)
        type MicroserviceResult<'S, 'E> =
        | Success of 'S 
        | Failure of MicroserviceError<'E> * RetryableError

        (** General response for all API Engine Requests **)
        type APIMicroserviceResponse = {
            jobID      : Guid
            jobType    : string
            request    : string
            resultKeys : string list option (** A collection of Riak keys, for the chunked results **)
            processed  : DateTime
            success    : bool
            retryable  : bool
            error      : string             (** Serialised error object **)
            isCacheHit : bool
        }

        (** Used by clients to understand which of (potentially) many microservices (of a given type) has best availability **)
        type MicroserviceHeartbeat = {
            serviceId   : Guid
            serviceType : string
            stamp       : DateTime
        } 