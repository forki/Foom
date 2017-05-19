﻿namespace Foom.Game.Sprite

open System
open System.Numerics
open System.Collections.Generic

open Foom.Ecs
open Foom.Renderer
open Foom.Collections
open Foom.Renderer
open Foom.Renderer.RendererSystem
open Foom.Game.Assets
open Foom.Game.Core

[<AutoOpen>]
module SpriteHelpers =

    let createSpriteColor lightLevel =
        let color = Array.init 6 (fun _ -> Vector4 (255.f, lightLevel, lightLevel, lightLevel))
        color
        |> Array.map (fun c ->
            Vector4 (
                c.X / 255.f,
                c.Y / 255.f,
                c.Z / 255.f,
                c.W / 255.f)
        )

    let vertices =
        [|
            Vector3 (-1.f, 0.f, 0.f)
            Vector3 (1.f, 0.f, 0.f)
            Vector3 (1.f, 0.f, 1.f)
            Vector3 (1.f, 0.f, 1.f)
            Vector3 (-1.f, 0.f, 1.f)
            Vector3 (-1.f, 0.f, 0.f)
        |]

    let uv =
        [|
            Vector2 (0.f, 0.f * -1.f)
            Vector2 (1.f, 0.f * -1.f)
            Vector2 (1.f, 1.f * -1.f)
            Vector2 (1.f, 1.f * -1.f)
            Vector2 (0.f, 1.f * -1.f)
            Vector2 (0.f, 0.f * -1.f)
        |]

type SpriteBatchInput (shaderInput) =
    inherit MeshInput (shaderInput)

    member val Center = shaderInput.CreateVertexAttributeVar<Vector3Buffer> ("in_center")

    member val Positions = shaderInput.CreateInstanceAttributeVar<Vector3Buffer> ("instance_position")

    member val LightLevels = shaderInput.CreateInstanceAttributeVar<Vector4Buffer> ("instance_lightLevel")

    member val UvOffsets = shaderInput.CreateInstanceAttributeVar<Vector4Buffer> ("instance_uvOffset")

type SpriteBatch (lightLevel) =
    inherit Mesh<SpriteBatchInput> (vertices, uv, createSpriteColor lightLevel)

    member val Positions = Buffer.createVector3 [||]

    member val LightLevels = Buffer.createVector4 [||]

    member val UvOffsets = Buffer.createVector4 [||]

    override this.SetShaderInput input =
        base.SetShaderInput input

        input.Positions.Set this.Positions
        input.LightLevels.Set this.LightLevels
        input.UvOffsets.Set this.UvOffsets

type SpriteBatchRendererComponent (layer, material, lightLevel) =
    inherit MeshRendererComponent<SpriteBatchInput, SpriteBatch> (layer, material, SpriteBatch lightLevel)

    member val SpriteCount = 0 with get, set

    member val Positions : Vector3 [] = Array.zeroCreate 1000000

    member val LightLevels : Vector4 [] = Array.zeroCreate 1000000

    member val UvOffsets : Vector4 [] = Array.zeroCreate 100000

[<Sealed>]
type SpriteComponent (layer : int, textureKind : TextureKind, lightLevel: int) =
    inherit Component ()

    member val Layer = layer

    member val Frame = 0 with get, set

    member val LightLevel = lightLevel with get, set

    member val TextureKind = textureKind

    member val RendererComponent : SpriteBatchRendererComponent = Unchecked.defaultof<SpriteBatchRendererComponent> with get, set

module Sprite =

    let shader = CreateShader SpriteBatchInput 0 (CreateShaderPass (fun _ -> []) "Sprite")

    let update (am: AssetManager) : Behavior<float32 * float32> =
        let lookup = Dictionary<int * TextureKind, SpriteBatchRendererComponent> ()

        Behavior.merge
            [
                Behavior.handleComponentAdded (fun ent (comp: SpriteComponent) _ em ->
                    let rendererComp = 
                        let key = (comp.Layer, comp.TextureKind)
                        match lookup.TryGetValue (key) with
                        | true, x -> x
                        | _ ->
                            let material = MaterialDescription<SpriteBatchInput> (shader, comp.TextureKind)
                            let rendererComp = new SpriteBatchRendererComponent(comp.Layer, material, 255.f)

                            let rendererEnt = em.Spawn ()
                            em.Add (rendererEnt, rendererComp)

                            lookup.[key] <- rendererComp

                            rendererComp
                        
                    comp.RendererComponent <- rendererComp
                )

                Behavior.update (fun _ em ea ->
                    em.ForEach<TransformComponent, SpriteComponent> (fun _ transformComp spriteComp ->
                        let rendererComp = spriteComp.RendererComponent
                        if rendererComp.SpriteCount < rendererComp.Positions.Length then
                            let c = single spriteComp.LightLevel / 255.f

                            rendererComp.Positions.[rendererComp.SpriteCount] <- transformComp.Position
                            rendererComp.LightLevels.[rendererComp.SpriteCount] <- Vector4 (c, c, c, 1.f)

                            match rendererComp.Material with
                            | Some material ->
                                let frames = material.Texture.Frames
                                let frame = spriteComp.Frame
                                let frame = 
                                    if frame >= frames.Length then 0
                                    else frame
                                                 
                                rendererComp.UvOffsets.[rendererComp.SpriteCount] <- frames.[frame]
                            | _ -> ()

                            rendererComp.SpriteCount <- rendererComp.SpriteCount + 1
                    )
                )

                Behavior.update (fun _ em ea ->
                    em.ForEach<SpriteBatchRendererComponent> (fun _ rendererComp ->
                        rendererComp.Mesh.Positions.Set (rendererComp.Positions, rendererComp.SpriteCount)
                        rendererComp.Mesh.LightLevels.Set (rendererComp.LightLevels, rendererComp.SpriteCount)
                        rendererComp.Mesh.UvOffsets.Set (rendererComp.UvOffsets, rendererComp.SpriteCount)
                        rendererComp.SpriteCount <- 0
                    )
                )
            ]

