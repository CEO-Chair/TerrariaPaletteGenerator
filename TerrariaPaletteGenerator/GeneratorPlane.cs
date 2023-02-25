﻿using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using plane;
using plane.Graphics;
using plane.Graphics.Buffers;
using plane.Graphics.Shaders;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Plane = plane.Plane;

namespace TerrariaPaletteGenerator;

public class GeneratorPlane : Plane
{
    public ComputeShader? PaletteComputeShader;

    public ShaderResourceBuffer<Vector4>? TileColorBuffer;

    public ShaderResourceBuffer<Vector4>? WallColorBuffer;

    public ShaderResourceBuffer<Vector4>? PaintColorBuffer;

    public ShaderResourceBuffer<int>? TilesForPixelArtBuffer;

    public ShaderResourceBuffer<int>? WallsForPixelArtBuffer;

    public Texture3D? TileWallPaletteTexture;
    public Texture3D? TileWallPaletteStagingTexture;

    public Texture3D? PaintPaletteTexture;
    public Texture3D? PaintPaletteStagingTexture;

    public ConstantBuffer<GeneratorComputeShaderBuffer>? GeneratorConstantBuffer;

    public ComputeShader? PaletteVisualizerComputeShader;

    public Texture2D? PaletteVisualizationTexture;

    public Texture2D? PaletteVisualizationTextureCopy;

    private double GenerateTime = 0;

    public GeneratorPlane(string windowName)
        : base(windowName)
    {

    }

    public unsafe override void Load()
    {
        string path = Path.GetDirectoryName(typeof(GeneratorPlane).Assembly.Location)!;

        PaletteComputeShader = ShaderCompiler.CompileFromFile<ComputeShader>(Renderer!, Path.Combine(path, "Shaders", "PaletteComputeShader.hlsl"), "CSMain", ShaderModel.ComputeShader5_0);

        GeneratorConstantBuffer = new ConstantBuffer<GeneratorComputeShaderBuffer>(Renderer!);

        PaletteVisualizerComputeShader = ShaderCompiler.CompileFromFile<ComputeShader>(Renderer!, Path.Combine(path, "Shaders", "PaletteVisualizationShader.hlsl"), "CSMain", ShaderModel.ComputeShader5_0);

        using FileStream tileColorFileStream = new FileStream(Path.Combine(path, "Data", "tileColorInfo.bin"), FileMode.Open);
        using BinaryReader tileColorReader = new BinaryReader(tileColorFileStream);

        Vector4[] tileColors = new Vector4[tileColorReader.ReadInt32()];
        tileColorReader.Read(MemoryMarshal.AsBytes(tileColors.AsSpan()));

        Vector4[] wallColors = new Vector4[tileColorReader.ReadInt32()];
        tileColorReader.Read(MemoryMarshal.AsBytes(wallColors.AsSpan()));

        Vector4[] paintColors = new Vector4[tileColorReader.ReadInt32()];
        tileColorReader.Read(MemoryMarshal.AsBytes(paintColors.AsSpan()));

        using FileStream validTileFileStream = new FileStream(Path.Combine(path, "Data", "validTileWallInfo.bin"), FileMode.Open);
        using BinaryReader validTileReader = new BinaryReader(validTileFileStream);

        int[] tilesForPixelArt = new int[validTileReader.ReadInt32()];
        validTileReader.Read(MemoryMarshal.AsBytes(tilesForPixelArt.AsSpan()));

        int[] wallsForPixelArt = new int[validTileReader.ReadInt32()];
        validTileReader.Read(MemoryMarshal.AsBytes(wallsForPixelArt.AsSpan()));

        GeneratorConstantBuffer.Data.TilesForPixelArtLength = tilesForPixelArt.Length;
        GeneratorConstantBuffer.Data.WallsForPixelArtLength = wallsForPixelArt.Length;

        GeneratorConstantBuffer.WriteData();

        TileColorBuffer = new ShaderResourceBuffer<Vector4>(Renderer!, tileColors, Format.FormatR32G32B32A32Float);
        WallColorBuffer = new ShaderResourceBuffer<Vector4>(Renderer!, wallColors, Format.FormatR32G32B32A32Float);
        PaintColorBuffer = new ShaderResourceBuffer<Vector4>(Renderer!, paintColors, Format.FormatR32G32B32A32Float);

        TilesForPixelArtBuffer = new ShaderResourceBuffer<int>(Renderer!, tilesForPixelArt, Format.FormatR32Uint);
        WallsForPixelArtBuffer = new ShaderResourceBuffer<int>(Renderer!, wallsForPixelArt, Format.FormatR32Uint);

        TileWallPaletteTexture = new Texture3D(Renderer!, 256, 256, 256, TextureType.None, BindFlag.UnorderedAccess, Format.FormatR32Uint);
        TileWallPaletteStagingTexture = new Texture3D(Renderer!, 256, 256, 256, TextureType.None, BindFlag.None, Format.FormatR32Uint, Usage.Staging, CpuAccessFlag.Read);

        PaintPaletteTexture = new Texture3D(Renderer!, 256, 256, 256, TextureType.None, BindFlag.UnorderedAccess, Format.FormatR8Uint);
        PaintPaletteStagingTexture = new Texture3D(Renderer!, 256, 256, 256, TextureType.None, BindFlag.None, Format.FormatR8Uint, Usage.Staging, CpuAccessFlag.Read);

        PaletteVisualizationTexture = new Texture2D(Renderer!, 4096, 4096, TextureType.None, sampleDesc: null, bindFlags: BindFlag.UnorderedAccess);
        PaletteVisualizationTextureCopy = new Texture2D(Renderer!, 4096, 4096, TextureType.None, sampleDesc: null, bindFlags: BindFlag.ShaderResource);
    }

    public override void Render()
    {

    }

    public override void RenderImGui()
    {
        ImGui.Begin("Generator");

        if (ImGui.Button("Generate"))
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Restart();

            GeneratePalette();

            stopwatch.Stop();
            GenerateTime = stopwatch.Elapsed.TotalSeconds;

            GeneratePaletteVisualization();
        }

        unsafe
        {
            ImGui.Image((nint)PaletteVisualizationTextureCopy!.ShaderResourceView.Handle, new Vector2(1024, 1024));
        }

        ImGui.Text($"{GenerateTime}");

        ImGui.End();
    }

    private void GeneratePalette()
    {
        PaletteComputeShader!.Bind();

        unsafe
        {
            TileColorBuffer!.Bind(0, BindTo.ComputeShader);
            WallColorBuffer!.Bind(1, BindTo.ComputeShader);
            PaintColorBuffer!.Bind(2, BindTo.ComputeShader);

            TilesForPixelArtBuffer!.Bind(3, BindTo.ComputeShader);
            WallsForPixelArtBuffer!.Bind(4, BindTo.ComputeShader);

            Renderer!.Context.CSSetUnorderedAccessViews(0, 1, ref TileWallPaletteTexture!.UnorderedAccessView, (uint*)null);
            Renderer!.Context.CSSetUnorderedAccessViews(1, 1, ref PaintPaletteTexture!.UnorderedAccessView, (uint*)null);
        }

        GeneratorConstantBuffer!.Bind(0, BindTo.ComputeShader);

        Renderer!.Context.Dispatch(26, 26, 26);

        Renderer!.Context.CopyResource(TileWallPaletteStagingTexture!.NativeTexture, TileWallPaletteTexture.NativeTexture);
        Renderer!.Context.CopyResource(PaintPaletteStagingTexture!.NativeTexture, PaintPaletteTexture.NativeTexture);

        using FileStream fs = new FileStream(Path.Combine(Path.GetDirectoryName(typeof(GeneratorPlane).Assembly.Location)!, "Data", "palette.bin"), FileMode.OpenOrCreate);

        ReadOnlySpan<uint> tileWallPaletteData = TileWallPaletteStagingTexture.MapReadSpan<uint>();
        fs.Write(MemoryMarshal.AsBytes(tileWallPaletteData));
        TileWallPaletteStagingTexture.Unmap();

        ReadOnlySpan<byte> paintPaletteData = PaintPaletteStagingTexture.MapReadSpan<byte>();
        fs.Write(paintPaletteData);
        PaintPaletteStagingTexture.Unmap();
    }

    private void GeneratePaletteVisualization()
    {
        PaletteVisualizerComputeShader!.Bind();

        unsafe
        {
            TileColorBuffer!.Bind(0, BindTo.ComputeShader);
            WallColorBuffer!.Bind(1, BindTo.ComputeShader);
            PaintColorBuffer!.Bind(2, BindTo.ComputeShader);

            Renderer!.Context.CSSetUnorderedAccessViews(0, 1, ref PaletteVisualizationTexture!.UnorderedAccessView, (uint*)null);

            Renderer!.Context.CSSetUnorderedAccessViews(1, 1, ref TileWallPaletteTexture!.UnorderedAccessView, (uint*)null);
            Renderer!.Context.CSSetUnorderedAccessViews(2, 1, ref PaintPaletteTexture!.UnorderedAccessView, (uint*)null);
        }

        Renderer!.Context.Dispatch(4096 / 32, 4096 / 32, 1);

        Renderer!.Context.CopyResource(PaletteVisualizationTextureCopy!.NativeTexture, PaletteVisualizationTexture!.NativeTexture);
    }
}

[StructAlign16]
public partial struct GeneratorComputeShaderBuffer
{
    public int TilesForPixelArtLength;
    public int WallsForPixelArtLength;

    public GeneratorComputeShaderBuffer()
    {
        TilesForPixelArtLength = 0;
        WallsForPixelArtLength = 0;
    }
}