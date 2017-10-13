//#r @"bin\Debug\Newtonsoft.Json.dll"
//#r @"bin\Debug\Sigma-Types-Job.dll"
//#r @"bin\Debug\Sigma-Lib-Web.dll"
//#r @"bin\Debug\Sigma_Types_AmazonAdvertising.dll"
//#r @"bin\Debug\Sigma_AmazonAdvertising_Connector.dll"
//
//open System
//open System.Net
//open System.Linq
//open System.Text
//open System.Collections.Generic
//open Newtonsoft.Json
//open Sigma.Lib.Web
//open Sigma.Types.API
//open Sigma.APIs.Connectors
//
//
//let refreshTokenRequest : GetRefreshTokenRequest = 
//    { client_id = "amzn1.application-oa2-client.ec3f20db7e794ac09ede0a61cec8d6e8"
//      client_secret = "de147d00c1eb823109a332a5c9a35c9cefd9f68b3a3556dabd3d919309499cdf"
//      refresh_token = "Atzr|IwEBIPsHV6gVGzHmiYkeURIztMACZ5UQv0W1m837QiRwwT8SdXPz1ggDPXH8QkvV1PLmV8V7RDmoS62ZsQvH3fb_gylmOdJ8HZfL4-CLoupkcO4orE07n6rRNM5NHcUYlYfzNOpqFd0rVA8VsvRwyyPpEQLE5hEyPDScJr2_MaE9PFbKqS81ZhfwOaMeZRm-0T3vAioUj2gaLOKH0THLFvUtfc9TWsWfhxYaRsV6cIr4XEuOCNJxtx9wqKTbdMeYaeoLbvUSTlApM12Zkmxu6RXQx8ugtler_XZ-c9iMZMuV4GEKwadKjXrH0dDK9nRKNv5Ks6ecnbgBBKc8Jd3xJdbb8NIfBykViPZtAgJIqb4uTZkrrgCGoHEg-TvRXStwqEtyOi24w7xGjWZ_pz-jY1K6FoIY6Hog_U4WxMpvgskPZBF-z-J8mq1gngIVFCDqH0TlfvI_yhWdywFr9zzPDjXEyYFvaQCEk03iBtKKjaru9pDzxlI23Zlcol76vCdZcptdrS2vFurCzR2cuMYbwQUk589dsUKOIa6cgORjPqkazPmj7g"}
//
//let response = AmazonAdvertising.GetAWSAppAccessTokenUsingRefreshToken refreshTokenRequest
//
//
//let appToken = match response with 
//               | Results x -> x |> Some
//               | AmazonAdvertisingError e -> None
//
//let dd = appToken.Value.access_token
//
//let countryCode : CountryCode = 
//    { countryCode = "CA"}
//
//let createProfileResponse = AmazonAdvertising.CreateProfile dd  countryCode
//
//let accToken = "Atza|IwEBIDEKk-iK3uuSxlK9NGyXD1qMxjL3RPNWhj2ukQWWC3yEzrokIuyTtXj5QiRReGVeaGvm219luacuY0iU8Nt7a7qV-B_6F5ki45hHM76sXjz5q6EtXguvomiS4N0RZ4qh2i_3qEZQ9Xj7vZBOyuCmzdq8e9tvxUrREA_PE_6umltrf4QwTtoBjZBXd7MJpD2JJTmEuauP-N4oKFwLB3SVHQHc0cxGJSQMImw1Lh_WdTCuhbh9yj7MiAPVDkDAhcefx2E-mLo2n2Ydpo-SeZZuaMRjXUX9mYV-JOIP1QKD8yJJAm-cnwzlnxGrEg9dm-qF-LLLghcK_esHO_VEX9p1ZKVTfdJ43RwHOTblh35V8j6fj_ApVkmgVZkvZ8LluBOePgp3oZrUhOKXxWTmelpIx5K226NBAQi4Xvb-ywfKyjt18vsiEyJD5Lmj50Fz6m9rHzRQvWXh8zcGpO-bWV0e1vlLGEAqFApb_CqMQaCiShRTuV5rUNU5ELFTWT5ZiSPFqS1R1N5oI2YL2qm-2DWUDoAz"
//
//let campaign = AmazonAdvertising.GetCampaigns accToken "174264926639635" "887127280979206"
//let profile = AmazonAdvertising.GetProfile dd "711058776241330"
//
//let associatedProfiles = AmazonAdvertising.GetAssociatedProfiles dd
//
//let reportRequest : AmazonReportRequest = 
//    { campaignType = "sponsoredProducts"
//      reportDate ="20171012"
//      metrics ="impressions,clicks"}
//
//let reportRequestObj = AmazonAdvertising.CreateReport accToken "887127280979206" "campaigns" reportRequest
//
//let reportResponse = match reportRequestObj with 
//                       | Results x -> x |> Some
//                       | AmazonAdvertisingError e -> None
//
//let reportId : ReportId = reportResponse.Value.reportId
//
////amzn1.clicksAPI.v1.m1.59DEF165.6ec3c707-8dfb-4aab-af07-a99224b9f875
//
//let reportDownloadRequest = AmazonAdvertising.GetReportById accToken "887127280979206" reportId
//let reportOutput = match reportDownloadRequest with 
//                       | Results x -> x |> Some
//                       | AmazonAdvertisingError e -> None
//
//let reportlocation = reportOutput.Value.location
// 
