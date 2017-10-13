namespace Sigma.Lib
    
    open System
    
    module Web = 
        
        open System
        open System.Text
        open System.Net

        (** Exception types for API requests **)
        type WebRequestError = 
        | WebException of WebExceptionStatus 
        | GeneralWebException of string
        
        (** General response object for API requests **) 
        type WebRequestResponse = 
        | Response of string
        | Error of WebRequestError

        (** Attempts a GET request against an API, and return an APIResponse **) 
        type WebRequest with 
        
            static member Transact (url: string, ?headers: List<string * string>, ?contentType: string, ?decompression: DecompressionMethods,  ?requestMethod: string, ?postBody: string) =
                
                (** Set some defaults **)
                let headersDef = defaultArg headers []
                let requestMethodDef = defaultArg requestMethod "GET"
                let contentTypeDef = defaultArg contentType "application/json; charset=utf-8"
                let decompressionDef = defaultArg decompression DecompressionMethods.None

                try         
                    let webRequest = WebRequest.Create ( Uri (url) ) :?> HttpWebRequest
                    
                    (** Set properties**)
                    webRequest.ContentType <- contentTypeDef
                    webRequest.Method <- requestMethodDef
                    webRequest.AutomaticDecompression <- decompressionDef
                    
                    (** Insert headers **)
                    headersDef |> List.iter( fun header -> webRequest.Headers.Add(fst header, snd header) )
                    
                    (** If this is a PUT or POST **)
                    match requestMethodDef with 
                    | "PUT" | "POST" -> match postBody with 
                                        | None   -> ()
                                        | Some x -> using (webRequest.GetRequestStream()) (fun stream -> ASCIIEncoding.ASCII.GetBytes(x) |> fun bodyEnc -> stream.Write(bodyEnc, 0, bodyEnc.Length);) 
                    | _              -> ()

                    (** Get response **)
                    use webResponse = webRequest.GetResponse() 
                    use stream = webResponse.GetResponseStream() 
                    use reader = new IO.StreamReader(stream) 
                    reader.ReadToEnd() |> Response
                with 
                | :? WebException as ex -> WebException ex.Status |> Error
                | _ as ex -> GeneralWebException ex.Message |> Error

            (** Attempts a POST against an API, and return an APIResponse **) 
            static member pushJSONAPI requestMethod json (url: string) =
                try         
                    using (new WebClient()) (fun webClient -> 
                        webClient.Headers.[HttpRequestHeader.ContentType] <- "application/json"
                        webClient.UploadString(url, requestMethod, json) |> Response )
                with 
                | :? WebException as ex -> WebException ex.Status |> Error
                | _ as ex -> GeneralWebException ex.Message |> Error