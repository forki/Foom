﻿module Foom.Renderer.RendererSystem

open System
open System.Numerics
open System.IO
open System.Drawing
open System.Collections.Generic

open Foom.Ecs
open Foom.Math
open Foom.Common.Components

type MeshInfo =
    {
        Position: Vector3 []
        Uv: Vector2 []
        Color: Color []
    }

type TextureInfo =
    {
        TexturePath: string
    }

type MaterialInfo =
    {
        TextureInfo: TextureInfo
        ShaderName: string
    }

type RenderInfo =
    {
        MeshInfo: MeshInfo
        MaterialInfo: MaterialInfo
        LayerIndex: int
    }

[<AbstractClass>]
type BaseMeshRendererComponent<'ExtraInfo, 'Extra> (meshInfo: MeshInfo, extraInfo: 'ExtraInfo) =

    member val MeshInfo = meshInfo

    member val ExtraInfo = extraInfo

    member val Mesh : Mesh =
        let color =
            meshInfo.Color
            |> Array.map (fun c ->
                Vector4 (
                    single c.R / 255.f,
                    single c.G / 255.f,
                    single c.B / 255.f,
                    single c.A / 255.f)
            )

        Mesh (meshInfo.Position, meshInfo.Uv, color)

    abstract member Extra : 'Extra

[<Sealed>]
type MeshRendererComponent (meshInfo) =
    inherit BaseMeshRendererComponent<obj, obj> (meshInfo, null)

    override val Extra = null

type SpriteInfo =
    {
        Center: Vector3 []
    }

type Sprite =
    {
        Center: Vector3Buffer
    }

type SpriteInput (shaderProgram) =
    inherit MeshInput (shaderProgram)

    member val Center = shaderProgram.CreateVertexAttributeVector3 ("in_center")

type SpriteRendererComponent (meshInfo, spriteInfo) =
    inherit BaseMeshRendererComponent<SpriteInfo, Sprite> (meshInfo, spriteInfo)

    override val Extra = 
        {
            Center = Buffer.createVector3 (spriteInfo.Center)
        }

type SkyInfo = SkyInfo of unit

type Sky = Sky of unit

type SkyRendererComponent (meshInfo) =
    inherit BaseMeshRendererComponent<SkyInfo, Sky> (meshInfo, SkyInfo ())

    override val Extra = Sky ()

type MeshRenderComponent (renderInfo) =

    member val RenderInfo = renderInfo

    member val Mesh : Mesh =
        let color =
            renderInfo.MeshInfo.Color
            |> Array.map (fun c ->
                Vector4 (
                    single c.R / 255.f,
                    single c.G / 255.f,
                    single c.B / 255.f,
                    single c.A / 255.f)
            )

        Mesh (renderInfo.MeshInfo.Position, renderInfo.MeshInfo.Uv, color)

    interface IComponent

type SpriteComponent (center) =

    member val Center = Buffer.createVector3 (center)

    interface IComponent

type FunctionCache = Dictionary<string, (EntityManager -> Entity -> Renderer -> obj) * (ShaderProgram -> obj -> (RenderPass -> unit) -> unit)>
type ShaderCache = Dictionary<string, ShaderId>
type TextureCache = Dictionary<string, Texture>

let handleSomething (functionCache: FunctionCache) (shaderCache: ShaderCache) (textureCache: TextureCache) (renderer: Renderer) =
    Behavior.handleEvent (fun (evt: Events.ComponentAdded<MeshRenderComponent>) _ em ->
        em.TryGet<MeshRenderComponent> (evt.Entity)
        |> Option.iter (fun comp ->

            let info = comp.RenderInfo
            let shaderName = info.MaterialInfo.ShaderName.ToUpper ()

            let shaderId, f =
                match shaderCache.TryGetValue (shaderName) with
                | true, shader ->

                    let f, _ =
                        match functionCache.TryGetValue(shaderName) with
                        | true, (f, g) -> f, g
                        | _ -> (fun _ _ _ -> null), (fun _ _ run -> run RenderPass.Depth)

                    shader, f
                | _ -> 

                    let f, g =
                        match functionCache.TryGetValue(shaderName) with
                        | true, (f, g) -> f, g
                        | _ -> (fun _ _ _ -> null), (fun _ _ run -> run RenderPass.Depth)

                    let shader = renderer.CreateTextureMeshShader (shaderName, DrawOperation.Normal, g)

                    shaderCache.Add (shaderName, shader)

                    shader, f

            let texture =
                match textureCache.TryGetValue (info.MaterialInfo.TextureInfo.TexturePath) with
                | true, texture -> texture
                | _ ->

                    let bmp = new Bitmap(info.MaterialInfo.TextureInfo.TexturePath)

                    let buffer = Texture2DBuffer ([||], 0, 0)
                    buffer.Set bmp

                    let texture =
                        {
                            Buffer = buffer
                        }

                    textureCache.Add(info.MaterialInfo.TextureInfo.TexturePath, texture)

                    texture

            renderer.TryAdd (shaderId, texture, comp.Mesh, f em evt.Entity renderer, info.LayerIndex) |> ignore
        )
    )

let handleCamera (renderer: Renderer) =
    Behavior.handleEvent (fun (evt: Events.ComponentAdded<CameraComponent>) ((time, deltaTime): float32 * float32) em ->
        em.TryGet<CameraComponent> (evt.Entity)
        |> Option.iter (fun cameraComp ->

            let settings =
                {
                    projection = cameraComp.Projection
                    depth = cameraComp.Depth
                    layerFlags = cameraComp.LayerFlags
                    clearFlags = cameraComp.ClearFlags
                }

            renderer.TryCreateRenderCamera settings
            |> Option.iter (fun camera -> cameraComp.Camera <- camera)
        )
    )

let create (shaders: (string * (EntityManager -> Entity -> Renderer -> obj) * (ShaderProgram -> obj -> (RenderPass -> unit) -> unit)) list) (app: Application) : Behavior<float32 * float32> =

    // This should probably be on the camera itself :)
    let zEasing = Foom.Math.Mathf.LerpEasing(0.100f)

    let renderer = Renderer.Create ()
    let functionCache = Dictionary<string, (EntityManager -> Entity -> Renderer -> obj) * (ShaderProgram -> obj -> (RenderPass -> unit) -> unit)> ()
    let shaderCache = Dictionary<string, ShaderId> ()
    let textureCache = Dictionary<string, Texture> ()

    shaders
    |> List.iter (fun (key, f, g) ->
        functionCache.[key.ToUpper()] <- (f, g)
    )

    Behavior.merge
        [
            handleCamera renderer
            handleSomething functionCache shaderCache textureCache renderer

            Behavior.update (fun ((time, deltaTime): float32 * float32) em _ ->

                em.ForEach<CameraComponent, TransformComponent> (fun ent cameraComp transformComp ->
                    let heightOffset = Mathf.lerp cameraComp.HeightOffsetLerp cameraComp.HeightOffset deltaTime

                    let projection = cameraComp.Projection
                    let mutable transform = Matrix4x4.Lerp (transformComp.TransformLerp, transformComp.Transform, deltaTime)

                    let mutable v = transform.Translation

                    v.Z <- zEasing.Update (transformComp.Position.Z, time)

                    transform.Translation <- v + Vector3(0.f,0.f,heightOffset)

                    let mutable invertedTransform = Matrix4x4.Identity

                    Matrix4x4.Invert(transform, &invertedTransform) |> ignore

                    let invertedTransform = invertedTransform

                    cameraComp.Camera.view <- invertedTransform
                    cameraComp.Camera.projection <- projection
                )

                renderer.Draw time

                Backend.draw app
            )

        ]
