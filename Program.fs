module Program

open System
open System.IO
open System.Collections.Concurrent
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Diagnosers
open BenchmarkDotNet.Configs
open BenchmarkDotNet.Jobs
open BenchmarkDotNet.Running
open BenchmarkDotNet.Validators
open BenchmarkDotNet.Environments

module Async =
  let unit = async.Zero ()
  let inline result x = async.Return x

  module Infixes =
    let inline (>>=) xA x2yA = async.Bind (xA, x2yA)
    let inline (>>-) xA x2y = async.Bind (xA, x2y >> result)

  module Extensions =
    module Seq =
      let inline iterAsync x2uA xs = async.For (xs, x2uA)

module Fibbonaci =
    open Hopac
    open Hopac.Infixes
    open System
    open System.Threading
    open System.Threading.Tasks
    open System.Diagnostics
    module SerialFun =
        let rec fib n =
            if n < 2L then
            n
            else
            fib (n-2L) + fib (n-1L)

    module SerialJob =
        
        let rec fib n = job {
            if n < 2L then
                return n
            else
                let! x = fib (n-2L)
                let! y = fib (n-1L)
                return x + y
        }

    module SerialOpt =
        let rec fib n =
            if n < 2L then
             Job.result n
            else
                fib ( n-2L) >>= fun x ->
                fib ( n-1L)>>- fun y ->
            x + y

    module ParallelJob =
        let rec fib n = job {
            if n < 2L then
                return n
            else
                let! (x, y) = fib (n-2L) <*> fib (n-1L)
                return x + y
        }

    module ParallelPro =
        let rec fib n =
            if n < 2L then
                Job.result n
            else
                fib (n-2L) |> Promise.start >>= fun xP ->
                fib (n-1L) >>= fun y ->
                xP >>- fun x ->
                x + y
    module ParallelOpt =
        let rec fib n =
            if n < 2L then
                Job.result n
            else
                fib <| n-2L 
                <*> Job.delay (fun () -> fib <| n-1L) 
                >>- fun (x, y) -> x + y
    module SerAsc =
        let rec fib n = async {
            if n < 2L then
                return n
            else
                let! x = fib <| n-2L
                let! y = fib <| n-1L
                return x + y
        }
    module OptAsc =
        open Async.Infixes

        let rec fib n =
            if n < 2L then
                Async.result n
            else
                fib <| n-2L >>= fun x ->
                fib <| n-1L >>= fun y ->
                Async.result (x + y)

    module ParAsc =
        let rec fib n = async {
            if n < 2L then
                return n
            else
                let! x = Async.StartChild (fib <| n-2L)
                let! y = fib <| n-1L
                let! x = x
                return x + y
        }

    module Task =
        let rec fib n =
            if n < 2L then
                n
            else
                let x = Task.Factory.StartNew (fun _ -> fib <| n-2L)
                let y = fib <| n-1L
                x.Result + y
open Fibbonaci
open Hopac
[<MemoryDiagnoser>]
type FibbonaciTests () =

    [<Params(10,20,30)>]
    member val public batchSize = 0L with get, set

    [<Benchmark>]
    member this.SerialFun() = SerialFun.fib this.batchSize
    [<Benchmark>]
    member this.SerialJob() = SerialJob.fib this.batchSize |> run
    [<Benchmark>]
    member this.SerialOpt() = SerialOpt.fib this.batchSize |> run

    [<Benchmark>]
    member this.ParallelJob() = ParallelJob.fib this.batchSize |> run
    [<Benchmark>]
    member this.ParallelPro() = ParallelPro.fib this.batchSize |> run
    [<Benchmark>]
    member this.ParallelOpt() = ParallelOpt.fib this.batchSize |> run
    [<Benchmark>]
    member this.SerAsc() = SerAsc.fib this.batchSize |> Async.RunSynchronously

    [<Benchmark>]
    member this.OptAsc() = OptAsc.fib this.batchSize |> Async.RunSynchronously


    [<Benchmark>]
    member this.ParAsc() = ParAsc.fib this.batchSize |> Async.RunSynchronously

    [<Benchmark>]
    member this.Task() = Task.fib this.batchSize 
        
        
        

open Npgsql
module PsqlHelpers =
    let execNonQuery connStr commandStr =
        use conn = new NpgsqlConnection(connStr)
        use cmd = new NpgsqlCommand(commandStr,conn)
        conn.Open()
        cmd.ExecuteNonQuery()
        

    let execNonQueryAsync connStr commandStr = async {

        use conn = new NpgsqlConnection(connStr)
        use cmd = new NpgsqlCommand(commandStr,conn)
        do! conn.OpenAsync() |> Async.AwaitTask
        return! cmd.ExecuteNonQueryAsync() |> Async.AwaitTask
    }

    let createDatabase connStr databaseName =
        databaseName
        |> sprintf "CREATE database \"%s\" ENCODING = 'UTF8'"
        |> execNonQuery connStr
        |> ignore
    let dropDatabase connStr databaseName =
        databaseName
        |> sprintf "select pg_terminate_backend(pid) from pg_stat_activity where datname='%s';"
        |> execNonQuery connStr
        |> ignore

        databaseName
        |> sprintf "DROP database \"%s\""
        |> execNonQuery connStr
        |> ignore

open PsqlHelpers

// [<MemoryDiagnoser>]
type PsqlTests () =

    let mutable databaseName = "defaultDatabase"

    
    let createCompanyTable connStr =
        let companyTable =  """
            CREATE TABLE COMPANY(
                ID UUID PRIMARY KEY     NOT NULL,
                NAME           TEXT    NOT NULL,
                AGE            INT     NOT NULL,
                ADDRESS        CHAR(50),
                SALARY         REAL
                );
        """
        companyTable
        |> execNonQuery connStr
        |> ignore
    

    let batch batchSize asyncs = async {
        let batches = asyncs
                    |> Seq.mapi (fun i a -> i, a)
                    |> Seq.groupBy (fst >> fun n -> n / batchSize)
                    |> Seq.map (snd >> Seq.map snd)
                    |> Seq.map Async.Parallel
        let results = ref []
        for batch in batches do
            let! result = batch
            results := (result :: !results)
        return (!results |> List.rev |> Seq.collect id |> Array.ofSeq)
    }
    let insertComapnyStr ()=
        sprintf
            """
            INSERT INTO COMPANY (ID,NAME,AGE,ADDRESS,SALARY) VALUES ('%s','Paul', 32, 'California', 20000.00 );
            """
            (Guid.NewGuid().ToString("n"))
    
    let createConnString host user pass database =
        sprintf "Host=%s;Username=%s;Password=%s;Database=%s" host user pass database

    let superConnStr = createConnString "localhost" "jimmybyrd" "postgres" "postgres"

    let dataConnStr () = createConnString "localhost" "jimmybyrd" "postgres" databaseName
  
    [<Setup>]
    member self.SetupData() = 
        databaseName <- Guid.NewGuid().ToString("n")
        createDatabase superConnStr databaseName
        dataConnStr () 
        |> createCompanyTable 

    [<Cleanup>]
    member self.Cleanup() = 
        dropDatabase superConnStr databaseName
    

    [<Params(1, 10, 100, 1000, 10000)>]
    member val public insertSize = 0 with get, set

    [<Params(1, 10, 50, 95, 950)>]
    member val public batchSize = 0 with get, set
  

    // [<Benchmark>]
    // member this.Insert() =
    //     for i in 1..this.insertSize do 
    //         insertComapnyStr()
    //         |> execNonQuery (dataConnStr ()) 
    //         |> ignore

    // [<Benchmark>]
    // member this.InsertAsync() = Async.StartAsTask( async {
    //     for i in 1..this.insertSize do
    //         do! insertComapnyStr ()
    //             |> execNonQueryAsync (dataConnStr ()) 
    //             |> Async.Ignore
    // } )


    [<Benchmark>]
    member this.InsertAsyncParallel() = Async.StartAsTask( async {

        // let asyncAtATime num list = async {
        //     let asyncs =
        //         list
        //         |> Seq.map(fun x ->
        //                 [1..num]
        //                 |> Seq.map x
        //                 |> (Async.Parallel >> Async.Ignore))
        //     for asy in asyncs do
        //         do! asy

        // }
        return!
            [1..this.insertSize]
            |> Seq.map(fun _ -> 
                insertComapnyStr ()
                    |> execNonQueryAsync (dataConnStr ()) 
            )
            |> batch this.batchSize

    } )
    

            
    // [<Benchmark>]
    // member this.Open() =
    //     use conn = new NpgsqlConnection(dataConnStr ())
    //     conn.Open()

    // [<Benchmark>]
    // member this.OpenAsync() =
    //     use conn = new NpgsqlConnection(dataConnStr ())
    //     conn.OpenAsync()
        

type AsyncComparer () =

    let asyncOp1 () = async { return () }
    let asyncOp2 thing = async {return thing}
    let asyncOp = async {
        do! asyncOp1()
        return! asyncOp2 42
    }

    [<Setup>]
    member self.SetupData() = 
        printfn "setup called"
    [<Cleanup>]
    member self.Cleanup() = 
        printfn "cleanup called"

    [<Benchmark>]
    member self.AsyncOp () = async {
        return! asyncOp
    }

    [<Benchmark>]
    member self.AsyncOpRunSync () = 
        asyncOp |> Async.RunSynchronously        

    [<Benchmark>]
    member self.AsyncStartAtTask () = 
        asyncOp |> Async.StartAsTask


    [<Benchmark>]
    member self.TaskFactoryStartNew () = 
        System.Threading.Tasks.Task.Factory.StartNew(fun () ->  42)


    [<Benchmark>]
    member self.TaskFactoryStartNewExecuteFsharpAsyncRunSynchronously () = 
        System.Threading.Tasks.Task.Factory.StartNew(fun () ->  asyncOp |> Async.RunSynchronously)



type SleepMarks () =
    [<Params(0, 1, 15, 100)>]
    member val public sleepTime = 0 with get, set

    [<Benchmark>]
    member this.Thread () = System.Threading.Thread.Sleep(this.sleepTime)

    [<Benchmark>]
    member this.Task () = System.Threading.Tasks.Task.Delay(this.sleepTime)

    //Doesn't seem to sleep
    [<Benchmark>]
    member this.Async () = Async.Sleep(this.sleepTime)
    //Doesn't seem to sleep
    [<Benchmark>]
    member this.AsyncReturn () = async {return! Async.Sleep(this.sleepTime)}


type BenchmarkSyncVsAsync () =

    let memStrm:MemoryStream = new MemoryStream()
    let mutable dst:byte array = null

    [<Params(256, 1024, 4096, 16384, 65536)>]
    member val public ArraySize = 0 with get, set

    [<Setup>]
    member this.Setup () =
        let arr = Array.zeroCreate<byte> this.ArraySize
        let rnd = System.Random()
        rnd.NextBytes arr
        memStrm.Write(arr, 0, this.ArraySize)
        dst <- Array.zeroCreate<byte> this.ArraySize

    [<Benchmark>]
    member this.Read () = 
       memStrm.Read( dst, 0, this.ArraySize )

    [<Benchmark>]
    member this.AsyncRead () = 
        async {
           return! memStrm.AsyncRead ( dst, 0, this.ArraySize )
        }

    [<Benchmark>]
    member this.ReadAsync () = 
        let tsk = memStrm.ReadAsync ( dst, 0, this.ArraySize )
        tsk.Wait()
        tsk.Result

let monoRunTime version =
    MonoRuntime(sprintf "Mono %s" version, sprintf @"/usr/local/Cellar/mono/%s/bin/mono" version)

let config =
     ManualConfig
            .Create(DefaultConfig.Instance)
        
            // .With(Job.ShortRun.With(monoRunTime "4.8.0.520" ))
            // .With(Job.MediumRun.With(monoRunTime "4.6.2.16" ))
            // .With(Job.MediumRun.With(monoRunTime "4.4.2.11" ))
            // .With(Job.MediumRun.With(monoRunTime "4.2.2.30" ))

            .With(Job.ShortRun.With(MonoRuntime()))
            .With(Job.ShortRun.With(CoreRuntime()))
            .With(ExecutionValidator.FailOnError)
open Fibbonaci
[<EntryPoint>]
let Main args =


    BenchmarkRunner.Run<FibbonaciTests>(config) |> ignore
    // BenchmarkRunner.Run<PsqlTests>(config) |> ignore
    // BenchmarkRunner.Run<BenchmarkSyncVsAsync>() |> ignore
    0


    