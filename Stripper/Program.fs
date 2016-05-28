open System.IO

// Learn more about F# at http://fsharp.org
// See the 'F# Tutorial' project for more help.

[<EntryPoint>]
let main argv = 
    printfn "%A" argv
    match argv with
    | [| "--in" ; inFile ; "--out" ; outFile |] ->
        use ``in`` = File.OpenRead inFile
        use out = File.Create outFile
        Hydra.ReferenceAssembly.createRefAsm ``in`` out
    | _ -> failwith "Invalid args: expected '--in <in file> --out <out file>'"
    0 // return an integer exit code
