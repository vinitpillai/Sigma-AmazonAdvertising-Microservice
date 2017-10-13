namespace Sigma.Lib
    
    open System

    [<AutoOpen>]
    module Riak = 
        
        open System.IO
        open System.Collections.Generic
        open System.Linq
        open System.Runtime.Serialization
        open System.Runtime.Serialization.Formatters.Binary
        open RiakClient
        open RiakClient.Models
        open Newtonsoft.Json

        type RiakHelper = 
        
            (** Adds a secondary index to a Riak object and returns the object **)
            static member AddRiakSecondaryIndex indexName (indexValue: string) (clearExisting: bool) (myRiakObject: RiakObject)  =
                myRiakObject.BinIndex(indexName)  |> fun obj -> match clearExisting with | true -> obj.Clear() | false -> obj 
                                                  |> fun obj -> obj.Set(indexValue) |> ignore
                myRiakObject

            (** Compose a Riak object **)
            static member Compose (bucketName: string) (key: string) (value: 'T) =  new RiakObject( bucketName, key, JsonConvert.SerializeObject <| value ) 
           
            (** Compose a Riak object without serialising it into JSON **)
            static member ComposeBinary (bucketName: string) (key: string) (value: 'T) = new RiakObject(bucketName, key, value)

            (** Commit a Riak Object **)
            static member Commit (riakClient: IRiakClient) (myObj: RiakObject) = 
                let putOptions =  new RiakPutOptions() |> fun x -> x.SetW (Quorum.WellKnown.All) |>  fun x -> x.SetDw (Quorum.WellKnown.All) 
                riakClient.Put( myObj, putOptions)
            
            (** Inserts a k/v pair and tacks on a secondary index **)
            static member Insert (riakClient: IRiakClient, bucketName: string, key: string, value: 'T, ?isDefBinary: bool) = 
                if ( defaultArg isDefBinary false ) then RiakHelper.ComposeBinary bucketName key value else RiakHelper.Compose bucketName key value 
                |> RiakHelper.Commit riakClient

            (** Inserts a k/v pair and tacks on a secondary index **)
            static member InsertWith2i (riakClient: IRiakClient) (bucketName: string) (key: string) (indexName: string) (indexValue: string) (toInsert: 'T) = 
                RiakHelper.Compose bucketName key toInsert |> RiakHelper.AddRiakSecondaryIndex indexName indexValue true |> RiakHelper.Commit riakClient 

            (** Inserts a k/v pair and tacks on a collection of secondary indexes **)
            static member InsertWith2iCol (riakClient: IRiakClient) (indicies: KeyValuePair<string,string> list) (toInsert: RiakObject) = 
                indicies |> List.fold (fun acc kvp -> RiakHelper.AddRiakSecondaryIndex kvp.Key kvp.Value false acc) toInsert |> RiakHelper.Commit riakClient

            (**Gets the index terms for a specified 2i with a string value **)
            static member GetKeyTermsWith2iByVal (riakClient: IRiakClient) bucketName indexName (indexValue: string) = 
                let indexId = new RiakIndexId(bucketName, indexName)
                let RiakIndexGetOptions = new RiakIndexGetOptions() |> fun x-> x.SetR(Quorum.WellKnown.All)
                let index = riakClient.GetSecondaryIndex(indexId, indexValue, RiakIndexGetOptions)
                match index.IsSuccess && index.Value.IndexKeyTerms.Count() > 0 with 
                | true -> index.Value.IndexKeyTerms |> Some
                | false -> None
            
            (** Gets all keys in a bucket with a specified 2i with a string value **)
            static member GetKeysWith2iByVal<'T> (riakClient: IRiakClient) bucketName indexName (indexValue: string) = 
                let keyTermsOpt = RiakHelper.GetKeyTermsWith2iByVal riakClient bucketName indexName indexValue
                let getOptions = new RiakGetOptions() |> fun x -> x.SetR(Quorum.WellKnown.All)
                match keyTermsOpt.IsSome with 
                | true -> keyTermsOpt.Value |> Seq.map ( fun item -> riakClient.Get(bucketName, item.Key, getOptions).Value.GetObject<'T>() ) |> Some
                | false -> None
                                
            (** Gets all keys in a bucket with a specified 2i within the max integer key range **)
            static member GetKeysWith2iByRange<'T> (riakClient: IRiakClient, bucketName, indexName, startRange: string, endRange: string) = 
                let indexId = new RiakIndexId(bucketName, indexName)
                let index = riakClient.GetSecondaryIndex(indexId, startRange, endRange)
                match index.IsSuccess with 
                | true -> index.Value.IndexKeyTerms |> Seq.map ( fun item -> riakClient.Get(bucketName, item.Key).Value.GetObject<'T>() ) |> Some
                | false -> None

            (** Gets the value with the specified key in a given bucket, as an option type **)
            static member GetValue<'T> (riakClient: IRiakClient) (bucket: string) (key: string) = 
                let getOptions = new RiakGetOptions() |> fun x -> x.SetR(Quorum.WellKnown.All)
                let result = riakClient.Get(bucket, key, getOptions)
                match result.IsSuccess with | true -> Some <| result.Value.GetObject<'T>() | false -> Console.WriteLine result.ErrorMessage ; None

            (** Gets the value with the key 'default' in a named bucket **)
            static member GetDefaultValue<'T> (riakClient: IRiakClient) (bucket: string) = RiakHelper.GetValue<'T> riakClient bucket "default"

            (** Save binary file **)
            static member InsertBinaryFile (riakClient: IRiakClient) (bucket: string) (targetKey: string) (sourceFilePath: string) = 
                 RiakHelper.Insert(riakClient, bucket, targetKey, File.ReadAllBytes sourceFilePath, true)
            
            (** Fetch binary file ***)
            static member GetBinaryFile (riakClient: IRiakClient) (bucket: string) (sourceKey: string) = 
                RiakHelper.GetValue<byte[]> riakClient bucket sourceKey
            
            (** Dump any object as a byte[] ***)
            static member DumpObj (riakClient: IRiakClient) (bucket: string) (key: string) (objectToStore: obj) = 
                let bf = new BinaryFormatter()
                let ms = new MemoryStream()
                bf.Serialize(ms, objectToStore)
                RiakHelper.Insert (riakClient, bucket, key, ms.ToArray(), true)

            (** Dump any object as a byte[] ***)
            static member GrabObj (riakClient: IRiakClient) (bucket: string) (key: string) =          
                let arrBytes = RiakHelper.GetValue<byte[]> riakClient bucket key
                let memStream = new MemoryStream()
                let binForm = new BinaryFormatter()
                memStream.Write(arrBytes.Value, 0, arrBytes.Value.Length)
                memStream.Seek(Convert.ToInt64(0), SeekOrigin.Begin) |> ignore
                binForm.Deserialize(memStream)

            (** Delete by key ***)
            static member DeleteByKey (riakClient: IRiakClient) (bucket: string) (key: string) = 
                let objToDelete = riakClient.Get(bucket, key) 
                match objToDelete.IsSuccess with 
                | true  -> riakClient.Delete(objToDelete.Value).IsSuccess
                | false -> false