open FSharp
open System.IO
open System.Linq
open System.Text.RegularExpressions

let testsPath = "src\\Ably.PubSub.Tests.Shared\\"


type SearchResult(lineNo:int,file:string,content:string seq) =
    member this.LineNo = lineNo
    member this.File = file
    member this.Content = content
    override this.ToString() =
        sprintf "%s,%s,%d" (this.Content |> Seq.last) this.File this.LineNo


let private read (file:string) =
    seq { use reader = new StreamReader(file)
          while not reader.EndOfStream do yield reader.ReadLine() }

let traitRegex = Regex(@"\[Trait\(\""(\w+)\"",\s*\""(\w+)\""\)\]")

let matchLine line = 
    let result = traitRegex.Match(line)
    match result.Success with
    | true -> (true, result.Groups.Cast<Group>() |> Seq.skip 1 |> Seq.map (fun g -> g.Value))
    | false -> (false, Seq.empty)

let search_file parse file =
    let isSpec (label:string) = 
        "spec" = (label.ToLower())

    read file
    |> Seq.mapi (fun i l -> i + 1, parse l)
    |> Seq.map (fun (i, l) -> (i, l, matchLine l))
    |> Seq.filter (fun (i, l, r) -> fst r)
    |> Seq.filter (fun (i, l, (_, values)) -> values |> Seq.head |> isSpec)
    |> Seq.map (fun (i, l, (success, value)) -> SearchResult(i, file, value))

let search parse =
    Seq.map (fun file -> search_file parse file)
    >> Seq.concat

let print_results<'a> : SearchResult seq -> unit =
    Seq.fold (fun c r -> printfn "%O" r; c + 1) 0
    >> printfn "%d results"

let save_results (results:SearchResult seq) =
    let lines = results |> Seq.map (fun i -> i.ToString()) |> Seq.toArray
    File.WriteAllLines("results.csv", lines)

Directory.GetFiles(testsPath, "*.cs", SearchOption.AllDirectories)
|> search id
|> save_results

