namespace Sigma.Lib
    
    open System
    
    [<AutoOpen>]
    module ListHelpers = 
            
        (* Converts a list into a list of lists, of a particular batch size *)
        let batchesOf size input = 
          // Inner function that does the actual work.
          // 'input' is the remaining part of the list, 'num' is the number of elements
          // in a current batch, which is stored in 'batch'. Finally, 'acc' is a list of
          // batches (in a reverse order)
          let rec loop input num batch acc =
            match input with
            | [] -> 
                // We've reached the end - add current batch to the list of all
                // batches if it is not empty and return batch (in the right order)
                if batch <> [] then (List.rev batch)::acc else acc
                |> List.rev
            | x::xs when num = size - 1 ->
                // We've reached the end of the batch - add the last element
                // and add batch to the list of batches.
                loop xs 0 [] ((List.rev (x::batch))::acc)
            | x::xs ->
                // Take one element from the input and add it to the current batch
                loop xs (num + 1) (x::batch) acc
          loop input 0 [] []
        
        (* Extensions *)
        type List<'a> with
            static member iterLen (action: ('T-> unit)) (collection: List<'T>) = 
                let collectionLength = List.length(collection)
                List.iter action collection
                collectionLength
            member this.toSeparated (char: char) = 
                this |> Seq.collect(fun x -> [x.ToString() + char.ToString()])
                     |> fun x -> String.Concat(x).TrimEnd(char) 
            member this.toDotSeparated () = this.toSeparated '.'
            member this.toCommaSeparated () = this.toSeparated ','
            member this.comb setSize = let rec doComb n l =
                                            match (n,l) with
                                            | ( 0 , _     ) -> [[]]
                                            | ( _ , []    ) -> []
                                            | ( n , x::xs ) -> let useX = List.map (fun l -> x::l) (doComb (n-1) xs)
                                                               let noX = doComb n xs
                                                               useX @ noX
                                       doComb setSize this
