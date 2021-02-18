using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Cube
{
    public static class CubeFaceConverter
    {
        private const float Pi = (float)Math.PI;
        private const float HalfPi = Pi / 2.0f;

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

            Parallel.ForEach(blocks, range =>
            {
                for (var k = range.Start; k < range.End; k++)
                {
                    var face = k / edge;
                    var j = k % edge;
                    var row = outputImages[face].GetPixelRowSpan(j);

                    for (var i = 0; i < edge; i++)
                    {
                        var xyz = OutputImageToVector(i, j, face, edge);
                        var color = InterpolateVectorToColor(xyz, inputImage, inputImageWidth, inputImageHeight);
                        row[i] = color;
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

        private static Rgb24 InterpolateVectorToColor(Vector3 xyz, Image<Rgb24> image, int imageWidth, int imageHeight)
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

            var ri = image.GetPixelRowSpan(vi);
            var r2 = image.GetPixelRowSpan(v2);

            // Pixel values of four nearest corners
            var a = ri[ui].ToVector4();
            var b = ri[u2].ToVector4();
            var c = r2[ui].ToVector4();
            var d = r2[u2].ToVector4();

            var result = new Rgb24();
            result.FromVector4(Vector4.Lerp(Vector4.Lerp(a, b, mu), Vector4.Lerp(c, d, mu), nu));
            return result;
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
