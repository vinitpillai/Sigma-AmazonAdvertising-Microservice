namespace Sigma.Lib
    
    open System
    
    [<AutoOpen>]
    module Hashing =
        
        open System.Security.Cryptography
        open System.Text

        (** Create an MD5 hash of the inpuyt value, which should be a string or a byte array **)
        let md5 (data : obj) : string =
            
            let input = 
                match data with 
                | :? String as myString -> System.Text.Encoding.UTF8.GetBytes myString
                | :? (byte array) as myByteArray -> myByteArray
                | _ -> System.Text.Encoding.UTF8.GetBytes ""

            (StringBuilder(), MD5.Create().ComputeHash(input))
            ||> Array.fold (fun builder bytes -> builder.Append(bytes.ToString("x2"))) |> string