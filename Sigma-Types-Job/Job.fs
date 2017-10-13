namespace Sigma.Types

    [<AutoOpen>]
    module Job =
        
        open System

        (** All job requests must implement this interface **)
        type IJobRequestInterface =
            abstract jobType       : string
            abstract jobID         : string
            abstract cacheLimit    : TimeSpan option 