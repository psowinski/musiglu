using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace Musiglu
{
   class Program
   {
      static void Main(string[] args)
      {
         Console.WriteLine("Musiglu ver. 0.1");

         if (args.Length > 0 && args[0] == "-a")
         {
            var input = args.Length > 0 ? args[1] : ".";
            var dirs = Directory.GetDirectories(input).ToList();
            dirs.ForEach(MergeFiles);
            dirs.Select(x => x + ".png").ToList().ForEach(x => Split(x, GetPageWidth(args)));
         }
         else if (args.Length > 0 && args[0] == "-g")
         {
            var input = args.Length > 0 ? args[0] : ".";
            Directory.GetDirectories(input).ToList().ForEach(MergeFiles);
         }
         else if (args.Length > 0 && args[0] == "-s")
         {
            Split(args[1], GetPageWidth(args));
         }
         else
         {
            Console.WriteLine("musiglue -g <dir> // merge images in subdirectories");
            Console.WriteLine("musiglue -g \"C:\\dir\"");
            Console.WriteLine("musiglue -g .");
            Console.WriteLine("musiglue -g // in current dir");
            Console.WriteLine("\nmusiglue -s <filename> -r1100// split single image");
            Console.WriteLine("\nmusiglue -a <dir> -r1100// merge and split by dir names");
            Console.WriteLine("-r<width> // -r is optional, default is 1100");
         }
      }

      private static int GetPageWidth(string[] args)
      {
         var sizeArg = args.FirstOrDefault(x => x.StartsWith("-r"));
         return string.IsNullOrEmpty(sizeArg) ? 1100 : int.Parse(sizeArg[2..]);
      }

      private static void Split(string file, int pageWidth = 1100)
      {
         using var srcBitmap = Bitmap.FromFile(file) as Bitmap;
         const int rowsPerPage = 10;

         var measures = DetectMeasures(srcBitmap);
         var pages = CalculatePages(measures, pageWidth, rowsPerPage);

         var pageHeight = srcBitmap.Height * rowsPerPage;
         var countPages = 1;
         var prevPoint = 0;
         foreach (var page in pages)
         {
            var pageRow = 0;
            using var destBitmap = new Bitmap(pageWidth, pageHeight);
            using var graphic = Graphics.FromImage(destBitmap);
            graphic.Clear(Color.FromArgb(0, 0, 0, 0)); //transparent

            foreach (var nextPoint in page.Select(g => g.x))
            {
               var width = nextPoint - prevPoint;
               if (width > pageWidth)
               {
                  Console.WriteLine($"Error - width {width} is too big. Generation has been stop for file {file}.");
                  return;
               }
               graphic.DrawImage(srcBitmap,
                  new Rectangle(0, srcBitmap.Height * pageRow++, width, srcBitmap.Height),
                  new Rectangle(prevPoint, 0, width, srcBitmap.Height),
                  GraphicsUnit.Pixel);
               prevPoint = nextPoint;
            }
            destBitmap.Save($"{Path.GetDirectoryName(file)}\\{Path.GetFileNameWithoutExtension(file)}-page_{countPages++}.png", ImageFormat.Png);
         }
      }

      private static IEnumerable<IGrouping<int, (int @group, int x)>> CalculatePages(List<int> measures, int pageWidth, int rowsPerPage)
      {
         var parts = CalculateWrappingPoints(measures, pageWidth);
         var pages = parts.Select((x, idx) => (group: idx / rowsPerPage, x)).GroupBy(x => x.@group);
         return pages;
      }

      private static List<int> CalculateWrappingPoints(List<int> measures, int pageWidth)
      {
         var parts = measures.Aggregate(new List<int>(), (acc, x) =>
         {
            if (acc.Count == 0)
               acc.Add(x);
            else
            {
               var edge = acc.Count > 1 ? acc[^2] : 0;
               if (x - edge < pageWidth)
                  acc[^1] = x;
               else
                  acc.Add(x);
            }

            return acc;
         });
         return parts;
      }

      private static List<int> DetectMeasures(Bitmap srcBitmap)
      {
         const int acceptanceLevel = 255 / 2;
         const int sampling = 50;
         const int half = sampling / 2;
         var heightHalf = srcBitmap.Height / 2;
         var sample = heightHalf / 5;

         var measures = Enumerable.Range(0, srcBitmap.Width)
            .Where(x =>
            {
               var alpha = srcBitmap.GetPixel(x, heightHalf).A; //leading line
               return alpha >= acceptanceLevel
                      && alpha <= srcBitmap.GetPixel(x, heightHalf - sample * 2).A //helper lines check
                      && alpha <= srcBitmap.GetPixel(x, heightHalf - sample).A
                      && alpha <= srcBitmap.GetPixel(x, heightHalf + sample).A
                      && alpha <= srcBitmap.GetPixel(x, heightHalf + sample * 2).A;
            })
            .GroupBy(x => x / sampling).Select(g => g.Max())
            .GroupBy(x => (x - half) / sampling).Select(g => g.Max())
            .ToList();
         return measures;
      }

      private static void MergeFiles(string dir)
      {
         Console.WriteLine($"Processing: {dir}");

         var files = GetFiles(dir);
         if (FindMissingFiles(files))
         {
            Console.WriteLine($"Missing part in {dir}");
            return;
         }
         MergeImages(dir, files);
      }

      private static void MergeImages(string dir, List<string> files)
      {
         var images = files.Select(Image.FromFile).ToList();

         var (width, height) = CalculateSize(dir, images);

         using var bitmap = new Bitmap(width, height);
         using var graphic = Graphics.FromImage(bitmap);

         var attr = ImageAttributes();
         var x = 0;
         foreach (var image in images)
         {
            graphic.DrawImage(image,
               new Rectangle(x, 0, image.Width, image.Height),
               0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attr);
            x += image.Width;
         }

         bitmap.Save($"{dir}.png", ImageFormat.Png);

         images.ForEach(x => x.Dispose());
      }

      private static (int, int) CalculateSize(string dir, List<Image> images)
      {
         var width = images.Select(x => x.Width).Aggregate(0, (acc, x) => acc + x);
         var height = images.Max(x => x.Height);

         if (images.Any(x => x.Height != height))
            Console.WriteLine($"Warning: Different heights in {dir}");
         return (width, height);
      }

      private static ImageAttributes ImageAttributes()
      {
         var attr = new ImageAttributes();
         attr.SetColorMatrix(new ColorMatrix(
            new[]
            {
               new[] {1.0F, 0.0F, 0.0F, 0.0F, 0.0F},
               new[] {0.0F, 1.0F, 0.0F, 0.0F, 0.0F},
               new[] {0.0F, 0.0F, 1.0F, 0.0F, 0.0F},
               new[] {0.0F, 0.0F, 0.0F, 1.0F, 0.0F},
               new[] {0.0F, 0.0F, 0.0F, 0.0F, 1.0F}
            }
         ));
         return attr;
      }

      private static List<string> GetFiles(string dir)
      {
         var files = Directory.GetFiles(dir)
            .Where(IsNumberedPng)
            .OrderBy(file => int.Parse(Path.GetFileNameWithoutExtension(file)))
            .ToList();
         return files;
      }

      private static bool FindMissingFiles(List<string> files)
      {
         return files.Where((x, idx) => int.Parse(Path.GetFileNameWithoutExtension(x)) != idx).Any();
      }

      private static bool IsNumberedPng(string file)
      {
         return Path.GetExtension(file) == ".png" &&
                int.TryParse(Path.GetFileNameWithoutExtension(file), out var number) 
                && number >= 0;
      }
   }
}
