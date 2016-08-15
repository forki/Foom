﻿[<RequireQualifiedAccess>]
module Foom.Client.Client

open System.Drawing
open System.Numerics
open System.Collections.Generic

open Foom.Renderer
open Foom.Wad
open Foom.Wad.Geometry
open Foom.Wad.Level
open Foom.Wad.Level.Structures

let inline lerp x y t = x + (y - x) * t

type UserState = {
    IsMapMoving: bool } with

    static member Default = { IsMapMoving = false }

type ClientState = 
    {
        Window: nativeint
        RenderUpdate: (float32 -> unit)
        User: UserState
        Level: Level 
    }

// These are sectors to look for and test to ensure things are working as they should.
// 568 - map10 sunder
// 4 - map10  sunder
// 4371 - map14 sunder
// 28 - e1m1 doom
// 933 - map10 sunder
// 20 - map10 sunder
// 151 - map10 sunder
// 439 - map08 sunder
// 271 - map03 sunder
// 663 - map05 sunder
// 506 - map04 sunder
// 3 - map02 sunder
// 3450 - map11 sunder
// 1558 - map11 sunder
// 240 - map07 sunder
// 2021 - map11 sunder

open Foom.Ecs
open Foom.Ecs.World
open Foom.Renderer.EntitySystem
open Foom.Common.Components

let init (world: World) =

    // Load up doom wads.

    let doom2Wad = Wad.create (System.IO.File.Open ("doom.wad", System.IO.FileMode.Open)) |> Async.RunSynchronously
    let wad = Wad.createFromWad doom2Wad (System.IO.File.Open ("sunder.wad", System.IO.FileMode.Open)) |> Async.RunSynchronously
    let lvl = Wad.findLevel "e1m1" doom2Wad |> Async.RunSynchronously


    // Extract all doom textures.

    Wad.flats doom2Wad
    |> Array.iter (fun tex ->
        let bmp = new Bitmap(64, 64, Imaging.PixelFormat.Format24bppRgb)

        for i = 0 to 64 - 1 do
            for j = 0 to 64 - 1 do
                let pixel = tex.Pixels.[i + (j * 64)]
                bmp.SetPixel (i, j, Color.FromArgb (int pixel.R, int pixel.G, int pixel.B))

        bmp.Save(tex.Name + ".bmp")
        bmp.Dispose ()
    )

    //Wad.flats wad
    //|> Array.iter (fun tex ->
    //    let bmp = new Bitmap(64, 64, Imaging.PixelFormat.Format24bppRgb)

    //    for i = 0 to 64 - 1 do
    //        for j = 0 to 64 - 1 do
    //            let pixel = tex.Pixels.[i + (j * 64)]
    //            bmp.SetPixel (i, j, Color.FromArgb (int pixel.R, int pixel.G, int pixel.B))

    //    bmp.Save(tex.Name + ".bmp")
    //    bmp.Dispose ()
    //)

    // Calculate polygons

    let sectorPolygons =
        lvl.Sectors
        //[| lvl.Sectors.[343] |]
        |> Array.mapi (fun i s -> 
            System.Diagnostics.Debug.WriteLine ("Sector " + string i)
            (Sector.polygonFlats s, s)
        )



    // Add entity system

    let app = Renderer.init ()
    let sys1 = Foom.Renderer.EntitySystem.create (app)
    let updateSys1 = world.AddSystem sys1

    let cameraEnt = world.EntityManager.Spawn ()
    world.EntityManager.AddComponent cameraEnt (CameraComponent ())
    world.EntityManager.AddComponent cameraEnt (TransformComponent (Matrix4x4.CreateTranslation (Vector3 (-3680.f, -6704.f, 64.f * 3.f))))
    world.EntityManager.AddComponent cameraEnt (CameraRotationComponent())

    let flatUnit = 64.f

    let mutable count = 0

    lvl.Sectors
    |> Array.iter (fun sector ->
        //sector
        Sector.wallTriangles lvl.Sectors sector
        |> Seq.iter (fun renderLinedef ->

            match Wad.tryFindTexture renderLinedef.TextureName doom2Wad with
            | None -> ()
            | Some tex ->

            let width = Array2D.length1 tex.Data
            let height = Array2D.length2 tex.Data

            let bmp = new Bitmap(width, height, Imaging.PixelFormat.Format24bppRgb)

            tex.Data
            |> Array2D.iteri (fun i j pixel ->
                bmp.SetPixel (i, j, Color.FromArgb (int pixel.R, int pixel.G, int pixel.B))
            )

            bmp.Save (tex.Name + ".bmp")
            bmp.Dispose ()


            let vertices = renderLinedef.Vertices
            let uv = Array.zeroCreate vertices.Length

            let mutable i = 0
            while (i < vertices.Length) do
                let p1 = vertices.[i]
                let p2 = vertices.[i + 1]
                let p3 = vertices.[i + 2]

                let width = single width
                let height = single height

                let v1 = Vector2 (p1.X, p1.Y)
                let v2 = Vector2 (p2.X, p2.Y)
                let v3 = Vector2 (p3.X, p3.Y)

                let one = 0.f
                let two = (v2 - v1).Length ()

                let x, y, z1, z3 =
                    if p3.Z < p1.Z then
                        (one + two) / width, 
                        0.f, 
                        1.f - ((abs (p1.Z - p3.Z)) / height * -1.f),
                        1.f
                    else
                        0.f, 
                        (one + two) / width, 
                        1.f,
                        1.f - ((abs (p1.Z - p3.Z)) / height * -1.f)

                uv.[i] <- Vector2 (x, z3)
                uv.[i + 1] <- Vector2(y, z3)
                uv.[i + 2] <- Vector2(y, z1)

                i <- i + 3

            let lightLevel = sector.LightLevel - 75
            let lightLevel =
                if lightLevel > 255 then 255
                else lightLevel

            let ent = world.EntityManager.Spawn ()

            world.EntityManager.AddComponent ent (TransformComponent (Matrix4x4.Identity))
            world.EntityManager.AddComponent ent (MeshComponent (vertices, uv))
            world.EntityManager.AddComponent ent (
                MaterialComponent (
                    "triangle.vertex",
                    "triangle.fragment",
                    tex.Name + ".bmp",
                    Color.FromArgb(lightLevel, lightLevel, lightLevel)
                )
            )
        )
    )

    sectorPolygons
    |> Array.iter (fun (polygons, sector) ->

        polygons
        |> List.iter (fun polygon ->
            let vertices =
                polygon
                |> Array.map (fun x -> [|x.X;x.Y;x.Z|])
                |> Array.reduce Array.append
                |> Array.map (fun x -> Vector3 (x.X, x.Y, single sector.FloorHeight))

            let uv = Array.zeroCreate vertices.Length

            let mutable i = 0
            while (i < vertices.Length) do
                let p1 = vertices.[i]
                let p2 = vertices.[i + 1]
                let p3 = vertices.[i + 2]

                uv.[i] <- Vector2 (p1.X / flatUnit, p1.Y / flatUnit * -1.f)
                uv.[i + 1] <- Vector2(p2.X / flatUnit, p2.Y / flatUnit * -1.f)
                uv.[i + 2] <- Vector2(p3.X / flatUnit, p3.Y / flatUnit * -1.f)

                i <- i + 3

            let lightLevel = sector.LightLevel - 75
            let lightLevel =
                if lightLevel > 255 then 255
                else lightLevel

            count <- count + 1
            let ent = world.EntityManager.Spawn ()

            world.EntityManager.AddComponent ent (TransformComponent (Matrix4x4.Identity))
            world.EntityManager.AddComponent ent (MeshComponent (vertices, uv))
            world.EntityManager.AddComponent ent (
                MaterialComponent (
                    "triangle.vertex",
                    "triangle.fragment",
                    sector.FloorTextureName + ".bmp",
                    Color.FromArgb(lightLevel, lightLevel, lightLevel)
                )
            )
        )
    )

    sectorPolygons
    |> Array.iter (fun (polygons, sector) ->

        polygons
        |> List.iter (fun polygon ->
            let vertices =
                polygon
                |> Array.map (fun x -> [|x.Z;x.Y;x.X|])
                |> Array.reduce Array.append
                |> Array.map (fun x -> Vector3 (x.X, x.Y, single sector.CeilingHeight))

            let uv = Array.zeroCreate vertices.Length

            let mutable i = 0
            while (i < vertices.Length) do
                let p1 = vertices.[i]
                let p2 = vertices.[i + 1]
                let p3 = vertices.[i + 2]

                uv.[i] <- Vector2 (p1.X / flatUnit, p1.Y / flatUnit * -1.f)
                uv.[i + 1] <- Vector2(p2.X / flatUnit, p2.Y / flatUnit * -1.f)
                uv.[i + 2] <- Vector2(p3.X / flatUnit, p3.Y / flatUnit * -1.f)

                i <- i + 3

            let lightLevel = sector.LightLevel - 75
            let lightLevel =
                if lightLevel > 255 then 255
                else lightLevel

            count <- count + 1
            let ent = world.EntityManager.Spawn ()

            world.EntityManager.AddComponent ent (TransformComponent (Matrix4x4.Identity))
            world.EntityManager.AddComponent ent (MeshComponent (vertices, uv))
            world.EntityManager.AddComponent ent (
                MaterialComponent (
                    "triangle.vertex",
                    "triangle.fragment",
                    sector.CeilingTextureName + ".bmp",
                    Color.FromArgb(lightLevel, lightLevel, lightLevel)
                )
            )
        )
    )

    printfn "COUNT: %A" count

    { 
        Window = app.Window
        RenderUpdate = updateSys1
        User = UserState.Default
        Level = lvl
    }

let draw t (prev: ClientState) (curr: ClientState) =

    let stopwatch = System.Diagnostics.Stopwatch.StartNew ()

    curr.RenderUpdate t

    stopwatch.Stop ()

    //printfn "%A" stopwatch.Elapsed.TotalMilliseconds
    //Renderer.clear ()

    //let projection = Matrix4x4.CreatePerspectiveFieldOfView (lerp prev.ViewDistance curr.ViewDistance t, (16.f / 9.f), 1.f, System.Single.MaxValue) |> Matrix4x4.Transpose
    //let rotation = Matrix4x4.CreateRotationX (-90.f * 0.0174533f) |> Matrix4x4.Transpose
    //let camera = Matrix4x4.CreateTranslation (Vector3 (0.f, -64.f * 8.f, 0.f) * -1.f) |> Matrix4x4.Transpose
    //let model = Matrix4x4.CreateTranslation (lerp prev.ViewPosition curr.ViewPosition t) |> Matrix4x4.Transpose
    //let mvp = (projection * rotation * camera * model) |> Matrix4x4.Transpose

    //Renderer.enableDepth ()
    //curr.Renderer.DrawVbo mvp
    //Renderer.disableDepth ()

    //Renderer.draw curr.Renderer.Application