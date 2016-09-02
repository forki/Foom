﻿module Foom.Renderer.RendererSystem

open System
open System.Numerics
open System.IO
open System.Drawing
open System.Collections.Generic

open Foom.Ecs
open Foom.Math
open Foom.Common.Components

////////

let render (projection: Matrix4x4) (view: Matrix4x4) (cameraModel: Matrix4x4) (entityManager: EntityManager) =
    let renderQueue = Queue<Mesh * int * int * Matrix4x4 * MaterialComponent * MeshComponent> ()
    let transparentQueue = ResizeArray<Mesh * int * int * Matrix4x4 * MaterialComponent * MeshComponent> ()

    entityManager.ForEach<MeshComponent, MaterialComponent, TransformComponent> (fun ent meshComp materialComp transformComp ->
        let model = transformComp.Transform

        let mvp = (projection * view) |> Matrix4x4.Transpose

        match meshComp.State, materialComp.TextureState, materialComp.ShaderProgramState with
        | MeshState.Loaded mesh, TextureState.Loaded textureId, ShaderProgramState.Loaded programId ->
                renderQueue.Enqueue ((mesh, textureId, programId, mvp, materialComp, meshComp))
        | _ -> ()
    )

    Renderer.enableDepth ()

    while (renderQueue.Count > 0) do
        let mesh, textureId, programId, mvp, materialComp, _ = renderQueue.Dequeue ()

        mesh.PositionBuffer.TryBufferData () |> ignore
        mesh.UvBuffer.TryBufferData () |> ignore

        Renderer.useProgram programId

        let uniformColor = Renderer.getUniformLocation programId "uni_color"
        let uniformProjection = Renderer.getUniformLocation programId "uni_projection"

        Renderer.setUniformProjection uniformProjection mvp
        Renderer.setTexture programId textureId

        mesh.PositionBuffer.Bind ()
        Renderer.bindPosition programId

        mesh.UvBuffer.Bind ()
        Renderer.bindUv programId

        Renderer.bindTexture textureId

        Renderer.setUniformColor uniformColor (Color.FromArgb (255, int materialComp.Color.R, int materialComp.Color.G, int materialComp.Color.B) |> RenderColor.OfColor)
        Renderer.drawTriangles 0 mesh.PositionBuffer.Length

    Renderer.disableDepth ()

    entityManager.ForEach<MaterialComponent, WireframeComponent> (fun ent materialComp wireframeComp ->

        let mvp = (projection * view) |> Matrix4x4.Transpose

        match wireframeComp.State, materialComp.ShaderProgramState with
        | WireframeState.Loaded mesh, ShaderProgramState.Loaded programId ->
            Renderer.useProgram programId

            let uniformColor = Renderer.getUniformColor programId
            let uniformProjection = Renderer.getUniformProjection programId

            Renderer.setUniformProjection uniformProjection mvp

            Renderer.bindVbo mesh.PositionBufferId
            Renderer.bindPosition programId

            Renderer.setUniformColor uniformColor (Color.FromArgb (255, int materialComp.Color.R, int materialComp.Color.G, int materialComp.Color.B) |> RenderColor.OfColor)
            Renderer.drawArrays 0 mesh.PositionBufferLength
        | _ -> ()
    )

let componentAddedQueue f =
    Behavior.eventQueue (fun (componentAdded: Events.ComponentAdded<'T>) (_, deltaTime: float32) entityManager ->
        entityManager.TryGet<'T> (componentAdded.Entity)
        |> Option.iter (fun comp ->
            f componentAdded.Entity comp deltaTime entityManager
        )
    )

let wireframeQueue =
    componentAddedQueue (fun ent (meshComp: WireframeComponent) deltaTime entityManager ->

        match meshComp.State with
        | WireframeState.ReadyToLoad (vertices) ->
            let vbo = Renderer.makeVbo ()
            Renderer.bufferVboVector3 vertices (sizeof<Vector3> * vertices.Length) vbo

            meshComp.State <- 
                WireframeState.Loaded
                    {
                        PositionBufferId = vbo
                        PositionBufferLength = vertices.Length
                    }
        | _ -> ()
     
    )

let meshQueue =
    componentAddedQueue (fun ent (meshComp: MeshComponent) deltaTime entityManager ->

        match meshComp.State with
        | MeshState.ReadyToLoad (vertices, uv) ->
            meshComp.State <- 
                MeshState.Loaded
                    {
                        PositionBuffer = Vector3ArrayBuffer (vertices)
                        UvBuffer = Vector2ArrayBuffer (uv)
                    }
        | _ -> ()

    )

let textureCache = Dictionary<string, int> ()
let shaderCache = Dictionary<string * string, int> ()
let materialQueue =
    componentAddedQueue (fun ent (materialComp: MaterialComponent) deltaTime entityManager ->

        match materialComp.TextureState with
        | TextureState.ReadyToLoad fileName ->

            match textureCache.TryGetValue (fileName) with
            | true, textureId ->
                materialComp.TextureState <- TextureState.Loaded textureId

            | _ ->
                try
                    use bmp = new Bitmap (fileName)
                    let bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb)

                    let textureId = Renderer.createTexture bmp.Width bmp.Height bmpData.Scan0

                    bmp.UnlockBits (bmpData)

                    materialComp.TextureState <- TextureState.Loaded textureId
                with | ex ->
                    printfn "%A" ex.Message

        | _ -> ()

        match materialComp.ShaderProgramState with
        | ShaderProgramState.ReadyToLoad (vertex, fragment) ->

            match shaderCache.TryGetValue ((vertex, fragment)) with
            | true, programId ->
                materialComp.ShaderProgramState <- ShaderProgramState.Loaded programId

            | _ ->
                let mutable vertexFile = ([|0uy|]) |> Array.append (File.ReadAllBytes (vertex))
                let mutable fragmentFile = ([|0uy|]) |> Array.append (File.ReadAllBytes (fragment))

                let programId = Renderer.loadShaders vertexFile fragmentFile
                materialComp.ShaderProgramState <- ShaderProgramState.Loaded programId
                shaderCache.Add ((vertex, fragment), programId)

        | _ -> ()

    )

let create (app: Application) : ESystem<float32 * float32> =

    let zEasing = Foom.Math.Mathf.LerpEasing(0.100f)

    ESystem.create "Renderer"
        [
            wireframeQueue
            meshQueue
            materialQueue

            Behavior.update (fun ((time, deltaTime): float32 * float32) entityManager eventManager ->

                Renderer.clear ()

                entityManager.TryFind<CameraComponent> (fun _ _ -> true)
                |> Option.iter (fun (ent, cameraComp) ->

                    entityManager.TryGet<TransformComponent> (ent)
                    |> Option.iter (fun transformComp ->

                        let heightOffset = Mathf.lerp cameraComp.HeightOffsetLerp cameraComp.HeightOffset deltaTime

                        let projection = cameraComp.Projection |> Matrix4x4.Transpose
                        let mutable transform = Matrix4x4.Lerp (transformComp.TransformLerp, transformComp.Transform, deltaTime)

                        let mutable v = transform.Translation


                        v.Z <- zEasing.Update (transformComp.Position.Z, time)

                        transform.Translation <- v + Vector3(0.f,0.f,heightOffset)

                        let mutable invertedTransform = Matrix4x4.Identity

                        Matrix4x4.Invert(transform, &invertedTransform) |> ignore

                        let invertedTransform = invertedTransform |> Matrix4x4.Transpose

                        render projection invertedTransform transformComp.Transform entityManager
                    )
                )

                Renderer.draw app

            )

        ]
