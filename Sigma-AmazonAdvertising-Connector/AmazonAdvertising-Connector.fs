namespace Sigma.APIs.Connectors

open System
open System.Net
open System.Collections.Generic
open Sigma.Lib.Web
open Newtonsoft.Json
open Sigma.Types.API.AmazonAdvertising_Types
open System.IO
open System.Text

type AmazonAdvertising() = 
    (** Primary API URLs template **)
    static member private AuthTokenURL = "https://api.amazon.com/auth/o2/token"
    static member private APICallURL = "https://advertising-api-test.amazon.com/v1/"

    (** Standard deserialisation mechanics **)
    static member PackageResponse<'T>(response : WebRequestResponse) = 
        try 
            response |> function 
            | WebRequestResponse.Response result -> 
                match result.Contains("error_description") with
                | true -> 
                    JsonConvert.DeserializeObject<AmazonAdvertisingAPIError>(result)
                    |> AmazonAdvertisingError.AmazonAdvertisingAPIError
                    |> AmazonAPIResponseWrapper.AmazonAdvertisingError
                | false -> JsonConvert.DeserializeObject<'T>(result) |> AmazonAPIResponseWrapper.Results
            | WebRequestResponse.Error error -> 
                error
                |> AmazonAdvertisingError.WebRequestError
                |> AmazonAPIResponseWrapper.AmazonAdvertisingError
        with ex -> 
            ex.Message
            |> AmazonAdvertisingError.GeneralError
            |> AmazonAPIResponseWrapper.AmazonAdvertisingError
    
    (** Get Amazon App access token **)
    static member GetAWSAppAccessToken(keys : OAuthKeys) = 
        (** Prepare request **)
        let oAuthUrl = AmazonAdvertising.AuthTokenURL
        let authHeaderContent = 
            String.Format
                ("Basic {0}", 
                 Convert.ToBase64String
                     (Encoding.UTF8.GetBytes
                          (Uri.EscapeDataString(keys.consumerKey) + ":" + Uri.EscapeDataString((keys.consumerSecret)))))
        
        let headers = 
            [ "Authorization", authHeaderContent
              "Accept-Encoding", "gzip" ]
        
        let contentType = "application/x-www-form-urlencoded;charset=UTF-8"
        let decompressonMethods = DecompressionMethods.GZip ||| DecompressionMethods.Deflate
        let postBody = "grant_type=client_credentials"
        (** Submit response **)
        WebRequest.Transact(oAuthUrl, headers, contentType, decompressonMethods, "POST", postBody) 
        |> fun response -> AmazonAdvertising.PackageResponse<GetAppTokenResponse> response

    (** Get Amazon App access token **)
//    static member GetAWSAppAccessTokenUsingRefreshToken(refreshTokenRequest : GetRefreshTokenRequest) = 
    static member GetAWSAppAccessTokenUsingRefreshToken(refreshToken : string) (clientId : string) ( clientSecret : string)  = 
         (** Prepare request **)
        let oAuthUrl = AmazonAdvertising.AuthTokenURL
        let refresTokenObj : RefreshTokenObj =  
            {   content_Type = "application/x-www-form-urlencoded;charset=UTF-8"
                client_secret =  clientSecret//refreshTokenRequest.client_secret
                grant_type ="refresh_token"
                client_id = clientId//refreshTokenRequest.client_id
                refresh_token = refreshToken//refreshTokenRequest.refresh_token
             }

        let postBody = JsonConvert.SerializeObject(refresTokenObj)
        (** Submit response **)
        WebRequest.Transact(url= oAuthUrl, requestMethod = "POST", postBody= postBody) 
        |> fun response -> AmazonAdvertising.PackageResponse<GetAppTokenResponse> response

    (**Create profile at Amazon**)
    static member CreateProfile (apptoken: string) (countryCode : CountryCode) =
        (** Prepare request **)
        let profileURL = String.Format("{0}profiles/register", AmazonAdvertising.APICallURL) 
        let headers = 
            [  "Authorization" , String.Format("{0} {1}", "bearer", apptoken)
               "Accept-Encoding", "gzip" ]
        let contentType = "application/json"
        let decompressonMethods = DecompressionMethods.GZip ||| DecompressionMethods.Deflate
        let postBody = JsonConvert.SerializeObject(countryCode)
        (** Submit response **)
        WebRequest.Transact( profileURL, headers, contentType, decompressonMethods, "PUT", postBody) 
        |> fun response -> AmazonAdvertising.PackageResponse<AmazonProfileResponse> response

    (**Get profile from Amazon**)
    static member GetAssociatedProfiles (apptoken: string) =
        (** Prepare request **)
        let profileURL = String.Format("{0}profiles", AmazonAdvertising.APICallURL) 
        let headers = 
            [  "Authorization" , String.Format("{0} {1}", "bearer", apptoken)]
        let contentType = "application/json"
        let decompressonMethods = DecompressionMethods.GZip ||| DecompressionMethods.Deflate
        (** Submit response **)
        WebRequest.Transact( profileURL, headers, contentType, decompressonMethods, "GET") 
        |> fun response -> AmazonAdvertising.PackageResponse<AmazonProfile list> response

    (**Get profile from Amazon**)
    static member GetProfile (apptoken: string) (profileId : string)  =
        (** Prepare request **)
        let profileURL = String.Format("{0}profiles/{1}", AmazonAdvertising.APICallURL, profileId) 
        let headers = 
            [  "Authorization" , String.Format("{0} {1}", "bearer", apptoken)]
        let contentType = "application/json"
        let decompressonMethods = DecompressionMethods.GZip ||| DecompressionMethods.Deflate
        (** Submit response **)
        WebRequest.Transact( profileURL, headers, contentType, decompressonMethods, "GET") 
        |> fun response -> AmazonAdvertising.PackageResponse<AmazonProfile> response

    (**Get campaign from Amazon**)
    static member GetCampaigns (apptoken: string) (campaignId: string) (profileId : string) =
        (** Prepare request **)
        let profileURL = String.Format("{0}campaigns/{1}", AmazonAdvertising.APICallURL , campaignId) 
        let headers = 
            [  "Authorization" , String.Format("{0} {1}", "bearer", apptoken)
               "Amazon-Advertising-API-Scope" , profileId
               "Accept-Encoding", "gzip"]
        let contentType = "application/json"
        let decompressonMethods = DecompressionMethods.GZip ||| DecompressionMethods.Deflate
        (** Submit response **)
        WebRequest.Transact( profileURL, headers, contentType, decompressonMethods, "GET") 
        |> fun response -> AmazonAdvertising.PackageResponse<AmazonCampaign> response

    (**Post report query to Amazon**)
    static member CreateReport (apptoken: string) (profileId: string) (recordType: string) (reportQuery : AmazonReportRequest) = 
        (** Prepare request **)
        let profileURL = String.Format("{0}{1}/report", AmazonAdvertising.APICallURL, recordType) 
        let headers = 
            [  "Authorization" , String.Format("{0} {1}", "bearer", apptoken)
               "Amazon-Advertising-API-Scope" , profileId ]
        let contentType = "application/json"
        let decompressonMethods = DecompressionMethods.GZip ||| DecompressionMethods.Deflate
        let postBody = JsonConvert.SerializeObject(reportQuery)
        (** Submit response **)
        WebRequest.Transact( profileURL, headers, contentType, decompressonMethods, "POST", postBody) 
        |> fun response -> AmazonAdvertising.PackageResponse<AmazonReportResponse> response

    (**Get query to Amazon**)
    static member GetReportById (apptoken: string) (profileId: string) (reportQuery : ReportId) =
        (** Prepare request **)
        let profileURL = String.Format("{0}{1}/download", AmazonAdvertising.APICallURL, reportQuery) 
        let headers = 
            [  "Authorization" , String.Format("{0} {1}", "bearer", apptoken)
               "Amazon-Advertising-API-Scope" , profileId 
               "Accept-Encoding","gzip"]
        let contentType = "application/json"
        let decompressonMethods = DecompressionMethods.GZip ||| DecompressionMethods.Deflate
        (** Submit response **)
        WebRequest.Transact( profileURL, headers, contentType, decompressonMethods, "GET") 
        |> fun response -> AmazonAdvertising.PackageResponse<AmazonReportResponse> response



