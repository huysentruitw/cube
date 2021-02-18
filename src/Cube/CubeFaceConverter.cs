using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

// #define ENABLE_BICUBIC_INTERPOLATION

namespace Cube
{
    public static class CubeFaceConverter
    {
        private const double Pi = Math.PI;
        private const double HalfPi = Pi / 2.0d;

        private static readonly JpegEncoder OutputEncoder = new JpegEncoder
        {
            Quality = 85,
        };

        public static async Task Convert(
            string inputImagePath,
            string outputImagePath,
            int cubeSizeInPixels = 4096,
            CancellationToken cancellationToken = default)
        {
            using var inputImage = await Image.LoadAsync<Rgb24>(Configuration.Default, inputImagePath, cancellationToken);

            var outputFaceImages = Enumerable
                .Range(0, 6)
                .Select(_ => new Image<Rgb24>(cubeSizeInPixels, cubeSizeInPixels, Color.White))
                .ToArray();

            ConvertBack(inputImage, outputFaceImages, cubeSizeInPixels);

            var saveTasks = outputFaceImages.Select(async (outputImage, index) =>
            {
                await outputImage.SaveAsync($"{outputImagePath}_{index}.jpg", OutputEncoder, cancellationToken);
                outputImage.Dispose();
            });

            await Task.WhenAll(saveTasks);
        }

        private static void ConvertBack(Image<Rgb24> inputImage, Image<Rgb24>[] outputImages, int edge)
        {
            var inputImageWidth = inputImage.Width;
            var inputImageHeight = inputImage.Height;

            var blocks = GenerateProcessingBlocks(6 * edge, Environment.ProcessorCount);

            Parallel.ForEach(blocks, ((int Start, int End) range) =>
            {
                for (var k = range.Start; k < range.End; k++)
                {
                    var face = k / edge;
                    var i = k % edge;

                    for (var j = 0; j < edge; j++)
                    {
                        var xyz = OutputImageToVector(i, j, face, edge);
                        var color = InterpolateVectorToColor(xyz, inputImage, inputImageWidth, inputImageHeight);
                        outputImages[face][i, j] = color;
                    }
                }
            });
        }

        private static Vector3 OutputImageToVector(int i, int j, int face, int edge)
        {
            var a = (2.0f * i) / edge - 1.0f;
            var b = (2.0f * j) / edge - 1.0f;

            return face switch
            {
                0 /* back   */ => new Vector3 { X = -1.0f, Y = -a, Z = -b },
                1 /* left   */ => new Vector3 { X = a, Y = -1.0f , Z = -b },
                2 /* front  */ => new Vector3 { X = 1.0f, Y = a, Z = -b },
                3 /* right  */ => new Vector3 { X = -a, Y = 1.0f, Z = -b },
                4 /* top    */ => new Vector3 { X = b, Y = a, Z = 1.0f },
                5 /* bottom */ => new Vector3 { X = -b, Y = a, Z = -1.0f },
                _ => throw new ArgumentOutOfRangeException(nameof(face), face, null),
            };
        }

        private static Rgb24 InterpolateVectorToColor(Vector3 xyz, Image<Rgb24> image, int imageWidth, int imageHeight)
        {
            var theta = Math.Atan2(xyz.Y, xyz.X); // # range -pi to pi
            var r = Math.Sqrt(xyz.X * xyz.X + xyz.Y * xyz.Y);
            var phi = Math.Atan2(xyz.Z, r); // # range -pi/2 to pi/2

            // source img coords
            var uf = (theta + Pi) / Pi * imageHeight;
            var vf = (HalfPi - phi) / Pi * imageHeight; // implicit assumption: _sh == _sw / 2

            // Use bilinear interpolation between the four surrounding pixels
            var ui = SafeIndex((int) uf, imageWidth);  //# coords of pixel to bottom left
            var vi = SafeIndex((int) vf, imageHeight);

#if ENABLE_BICUBIC_INTERPOLATION
            var u2 = SafeIndex(ui + 1, imageWidth);    //# coords of pixel to top right
            var v2 = SafeIndex(vi + 1, imageHeight);
            var mu = (byte)((uf - ui) * 255); //# fraction of way across pixel
            var nu = (byte)((vf - vi) * 255);

            // Pixel values of four nearest corners
            var a = image[ui, vi];
            var b = image[u2, vi];
            var c = image[ui, v2];
            var d = image[u2, v2];

            return Mix(Mix(a, b, mu), Mix(c, d, mu), nu);
#else
            return image[ui, vi];
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static int SafeIndex(int n, int size) => Math.Min(Math.Max(n, 0), size - 1);

#if ENABLE_BICUBIC_INTERPOLATION
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static Rgb24 Mix(Rgb24 one, Rgb24 other, byte c)
        {
            var red = (byte)(one.R + (other.R - one.R) * c);
            var green = (byte)(one.G + (other.G - one.G) * c);
            var blue = (byte)(one.B + (other.B - one.B) * c);
            return new Rgb24(red, green, blue);
        }
#endif

        private static IEnumerable<(int Start, int End)> GenerateProcessingBlocks(int range, int blockCount)
        {
            var blockSize = range / Math.Max(blockCount, 1);
            var start = 0;

            while (true)
            {
                var end = start + blockSize;
                if (end + blockSize > range)
                    end = range;

                yield return (start, end);

                if (end >= range)
                    break;

                start = end;
            }
        }
    }
}
