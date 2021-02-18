using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace Cube
{
    public static class CubeFaceConverter
    {
        private const float Pi = (float)Math.PI;
        private const float HalfPi = Pi / 2.0f;

        public static async Task Convert(
            string inputImagePath,
            string outputImagePath,
            int cubeSizeInPixels = 4096,
            CancellationToken cancellationToken = default)
        {
            using var inputImage = SKBitmap.Decode(inputImagePath);

            var outputImages = Enumerable
                .Range(0, 6)
                .Select(_ => new SKBitmap(cubeSizeInPixels, cubeSizeInPixels, SKColorType.Bgra8888, SKAlphaType.Opaque))
                .ToArray();

            ConvertBack(inputImage, outputImages, cubeSizeInPixels, cancellationToken);

            var saveTasks = outputImages.Select(async (outputImage, index) =>
            {
                await using var stream = File.OpenWrite($"{outputImagePath}_{index}.jpg");
                outputImage.Encode(SKEncodedImageFormat.Jpeg, 85)?.SaveTo(stream);
                await stream.FlushAsync(cancellationToken);
                outputImage.Dispose();
            });

            await Task.WhenAll(saveTasks);
        }

        private static void ConvertBack(SKBitmap inputImage, SKBitmap[] outputImages, int edge, CancellationToken cancellationToken)
        {
            IntPtr inputData = inputImage.GetPixels();
            IntPtr[] outputData = outputImages.Select(outputImage => outputImage.GetPixels()).ToArray();
            var inputImageWidth = inputImage.Width;
            var inputImageHeight = inputImage.Height;

            var blocks = GenerateProcessingBlocks(6 * edge, Environment.ProcessorCount);

            var options = new ParallelOptions
            {
                CancellationToken = cancellationToken,
            };

            Parallel.ForEach(blocks, options, range =>
            {
                unsafe
                {
                    for (var k = range.Start; k < range.End; k++)
                    {
                        var face = k / edge;
                        var j = k % edge;

                        for (var i = 0; i < edge; i++)
                        {
                            var xyz = OutputImageToVector(i, j, face, edge);
                            var color = InterpolateVectorToColor(xyz, (uint*)inputData, inputImageWidth, inputImageHeight);
                            *((uint*)outputData[face] + (i + j * edge)) = color;
                        }
                    }
                }
            });
        }

        private static Vector3 OutputImageToVector(int i, int j, int face, int edge)
        {
            var a = 2.0f * i / edge - 1.0f;
            var b = 2.0f * j / edge - 1.0f;

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

        private static unsafe uint InterpolateVectorToColor(Vector3 xyz, uint* imageData, int imageWidth, int imageHeight)
        {
            var theta = (float)Math.Atan2(xyz.Y, xyz.X); // # range -pi to pi
            var r = (float)Math.Sqrt(xyz.X * xyz.X + xyz.Y * xyz.Y);
            var phi = (float)Math.Atan2(xyz.Z, r); // # range -pi/2 to pi/2

            // Source image coordinates
            var uf = (theta + Pi) / Pi * imageHeight;
            var vf = (HalfPi - phi) / Pi * imageHeight; // implicit assumption: _sh == _sw / 2

            // Use bilinear interpolation between the four surrounding pixels
            var ui = Math.Clamp((int)uf, 0, imageWidth - 1);  // # coords of pixel to bottom left
            var vi = Math.Clamp((int)vf, 0, imageHeight - 1);

            var u2 = Math.Min(ui + 1, imageWidth - 1);    // # coords of pixel to top right
            var v2 = Math.Min(vi + 1, imageHeight - 1);
            var mu = uf - ui; // # fraction of way across pixel
            var nu = vf - vi;

            // Pixel values of four nearest corners
            var a = (byte*)(imageData + (ui + vi * imageWidth));
            var b = (byte*)(imageData + (u2 + vi * imageWidth));
            var c = (byte*)(imageData + (ui + v2 * imageWidth));
            var d = (byte*)(imageData + (u2 + v2 * imageWidth));

            var red1 = *(a + 0) + (*(b + 0) - *(a + 0)) * mu;
            var green1 = *(a + 1) + (*(b + 1) - *(a + 1)) * mu;
            var blue1 = *(a + 2) + (*(b + 2) - *(a + 2)) * mu;

            var red2 = *(c + 0) + (*(d + 0) - *(c + 0)) * mu;
            var green2 = *(c + 1) + (*(d + 1) - *(c + 1)) * mu;
            var blue2 = *(c + 2) + (*(d + 2) - *(c + 2)) * mu;

            var red = (byte)(red1 + (red2 - red1) * nu);
            var green = (byte)(green1 + (green2 - green1) * nu);
            var blue = (byte)(blue1 + (blue2 - blue1) * nu);

            return ((uint)blue << 16) + ((uint)green << 8) + red;
        }

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
