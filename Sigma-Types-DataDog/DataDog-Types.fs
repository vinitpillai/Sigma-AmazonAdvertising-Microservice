namespace Sigma.Types

    [<AutoOpen>]
    module DataDog =

        open System

        (** Corresponds to the alert statuses in DataDog **)
        type AlertType = 
           | Error
           | Warning
           | Info
           | Success