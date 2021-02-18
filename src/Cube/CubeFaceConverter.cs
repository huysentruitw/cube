using System;
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

            await ConvertBack(inputImage, outputFaceImages, cubeSizeInPixels);

            for (var i = 0; i < outputFaceImages.Length; i++)
            {
                await outputFaceImages[i].SaveAsync($"{outputImagePath}_{i}.jpg", OutputEncoder, cancellationToken);
                outputFaceImages[i].Dispose();
            }
        }

        private static Task ConvertBack(Image<Rgb24> inputImage, Image<Rgb24>[] outputImages, int edge)
        {
            for (var face = 0; face < 6; face++)
            {
                for (var i = 0; i < edge; i++)
                {
                    for (var j = 0; j < edge; j++)
                    {
                        var xyz = OutputImageToVector(i, j, face, edge);
                        var color = InterpolateVectorToColor(xyz, inputImage);
                        outputImages[face][i, j] = color;
                    }
                }
            }

            return Task.CompletedTask;
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

        private static Rgb24 InterpolateVectorToColor(Vector3 xyz, Image<Rgb24> image)
        {
            var _sw = image.Width;
            var _sh = image.Height;

            var theta = Math.Atan2(xyz.Y, xyz.X); // # range -pi to pi
            var r = Math.Sqrt(xyz.X * xyz.X + xyz.Y * xyz.Y);
            var phi = Math.Atan2(xyz.Z, r); // # range -pi/2 to pi/2

            // source img coords
            var uf = (theta + Math.PI) / Math.PI * _sh;
            var vf = (Math.PI / 2 - phi) / Math.PI * _sh; // implicit assumption: _sh == _sw / 2

            // Use bilinear interpolation between the four surrounding pixels
            var ui = SafeIndex((int) uf, _sw);  //# coords of pixel to bottom left
            var vi = SafeIndex((int) vf, _sh);
            var u2 = SafeIndex(ui + 1, _sw);    //# coords of pixel to top right
            var v2 = SafeIndex(vi + 1, _sh);
            var mu = uf - ui; //# fraction of way across pixel
            var nu = vf - vi;
            mu = nu = 0; // TODO: What ? :)

            // Pixel values of four nearest corners
            var A = image[ui, vi];
            var B = image[u2, vi];
            var C = image[ui, v2];
            var D = image[u2, v2];

            return Mix(Mix(A, B, mu), Mix(C, D, mu), nu);
        }

        private static int SafeIndex(int n, int size)
            => Math.Min(Math.Max(n, 0), size - 1);

        private static Rgb24 Mix(Rgb24 one, Rgb24 other, double c)
        {
            var red = (byte)(one.R + (other.R - one.R) * c);
            var green = (byte)(one.G + (other.G - one.G) * c);
            var blue = (byte)(one.B + (other.B - one.B) * c);
            return new Rgb24(red, green, blue);
        }
    }
}
