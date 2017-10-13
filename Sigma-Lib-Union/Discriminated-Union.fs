namespace Sigma.Lib
    
    open System
    
    [<AutoOpen>]
    module DiscriminatedUnion = 
        
        open Microsoft.FSharp.Reflection

        let toString (x:'a) = 
            match FSharpValue.GetUnionFields(x, typeof<'a>) with
            | case, _ -> case.Name

        let fromString<'a> (s:string) =
            match FSharpType.GetUnionCases typeof<'a> |> Array.filter (fun case -> case.Name = s) with
            |[|case|] -> Some(FSharpValue.MakeUnion(case,[||]) :?> 'a)
            |_ -> None