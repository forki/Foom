﻿open System
open System.IO
open System.Diagnostics
open System.Numerics
open System.Threading.Tasks

open Foom.Client
open Foom.Ecs
open Foom.Network
open Foom.Renderer
open Foom.Input
open Foom.Game.Assets
open Foom.Wad
open Foom.Export

let world = World (65536)

let start (invoke: Task ref) =
    let app = Backend.init ()
    let gl = OpenTKGL (app)
    let input = DesktopInput (app.Window)
    let assetLoader =
        {
            new IAssetLoader with

                member this.LoadTextureFile (assetPath) =
                    new BitmapTextureFile (assetPath) :> TextureFile
        }

    let loadTextFile = (fun filePath -> File.ReadAllText filePath |> System.Text.Encoding.UTF8.GetBytes)
    let openWad = (fun name -> System.IO.File.Open (name, FileMode.Open) :> Stream)
    let exportTextures =
        (fun wad _ ->
            wad |> exportFlatTextures
            wad |> exportTextures
            wad |> exportSpriteTextures
        )
    let client = Client.init gl assetLoader loadTextFile openWad exportTextures input world

    let stopwatch = System.Diagnostics.Stopwatch ()

    GameLoop.start 30.
        client.AlwaysUpdate
        (fun time interval ->
            stopwatch.Reset ()
            stopwatch.Start ()

            System.Threading.Thread.Sleep(1)
            GC.Collect (0)

            (!invoke).RunSynchronously ()
            invoke := (new Task (fun () -> ()))

            client.Update (
                TimeSpan.FromTicks(time).TotalSeconds |> single, 
                TimeSpan.FromTicks(interval).TotalSeconds |> single
            )

        )
        (fun currentTime t ->
            Client.draw (TimeSpan.FromTicks(currentTime).TotalSeconds |> single) t client client

            if stopwatch.IsRunning then
                stopwatch.Stop ()

                printfn "FPS: %A" (int (1000. / stopwatch.Elapsed.TotalMilliseconds))
        )

[<EntryPoint>]
let main argv =
    start (new Task (fun () -> ()) |> ref)
    0
