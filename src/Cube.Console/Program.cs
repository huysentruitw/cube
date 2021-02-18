using System;
using System.Diagnostics;
using System.IO;
using Cube;

const string input = @"C:\Users\woute\Downloads\input.bmp";
const string output = @"D:\temp\cube";
const int cubeSizeInPixels = 4096;

Console.WriteLine($"Convert equirectangular panorama [{Path.GetFileName(input)}] into cube face [{output}] with a dimension of {cubeSizeInPixels}x{cubeSizeInPixels} pixels");

var sw = new Stopwatch();

sw.Start();
CubeFaceConverter.Convert(input, output, cubeSizeInPixels).Wait();
sw.Stop();

Console.WriteLine($"Conversion took {sw.ElapsedMilliseconds} ms");
