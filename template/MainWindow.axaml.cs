using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ImageApp
{
    public partial class MainWindow : Window
    {
        private WriteableBitmap? _loadedBitmap;     // the raw loaded image, kept in color, for display in OriginalImage
        private byte[,,]? _loadedColorPixels;        // [x, y, channel] with channel 0=R, 1=G, 2=B -- extracted once at load time
        private byte[,]? _processedGray;             // Processed grayscale values (nullable)
        private int _width;
        private int _height;

        // Simple fixed defaults used by the functions below until you add your own
        // GUI controls (TextBoxes, ComboBoxes, etc.) to let the user set these values.
        private byte threshold = 128;

        // Enum for operations. As you implement each function, add a case for it
        // in OnApply below; the dropdown is populated automatically from this list.
        //
        // NOTE: Task1, Task2, and Task3 (from the assignment text) are NOT listed here.
        // You need to add those dropdown entries yourself as part of implementing them.
        private enum ProcessingFunctions
        {
            ConvertToGrayscale,
            InvertImage,
            AdjustContrast,
            ConvolveImage,
            MedianFilter,
            EdgeMagnitude,
            ThresholdImage,
            BinaryErodeImage,
            BinaryDilateImage,
            BinaryOpenImage,
            BinaryCloseImage,
            GrayscaleErodeImage,
            GrayscaleDilateImage
        }

        public MainWindow()
        {
            InitializeComponent();

            OperationBox.ItemsSource = Enum.GetNames(typeof(ProcessingFunctions));
            OperationBox.SelectedIndex = 0; // Select first item by default
        }

        // Load Image
        private async void OnLoadImage(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (this.StorageProvider == null) return;

            var files = await this.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Open Image",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } }
                    }
                });

            var file = files.FirstOrDefault();
            if (file == null) return;

            await using var stream = await file.OpenReadAsync();

            // Decode the file using Avalonia's own image loader 
            using var decoded = new Bitmap(stream);
            var size = decoded.PixelSize;
            _width = size.Width;
            _height = size.Height;

            // Force a known, fixed pixel layout (RGBA, 8 bits per channel, unpremultiplied alpha)
            // so we can reliably read raw bytes regardless of the source file's own format.
            _loadedBitmap?.Dispose();
            _loadedBitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);

            _loadedColorPixels = new byte[_width, _height, 3];
            using (var fb = _loadedBitmap.Lock())
            {
                decoded.CopyPixels(new PixelRect(0, 0, _width, _height), fb.Address, fb.RowBytes * _height, fb.RowBytes);
                // ^ transcodes the decoded image into our WriteableBitmap's RGBA8888 layout

                int totalBytes = fb.RowBytes * _height;
                byte[] buffer = new byte[totalBytes];
                Marshal.Copy(fb.Address, buffer, 0, totalBytes);

                for (int y = 0; y < _height; y++)
                {
                    int rowStart = y * fb.RowBytes;
                    for (int x = 0; x < _width; x++)
                    {
                        int idx = rowStart + x * 4; // 4 bytes per pixel: R, G, B, A
                        _loadedColorPixels[x, y, 0] = buffer[idx + 0];
                        _loadedColorPixels[x, y, 1] = buffer[idx + 1];
                        _loadedColorPixels[x, y, 2] = buffer[idx + 2];
                    }
                }
            }

            _processedGray = null;
            OriginalImage.Source = _loadedBitmap;
            ProcessedImage.Source = null;
        }

        // Save Processed Image
        private async void OnSaveImage(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_processedGray == null) return;
            if (this.StorageProvider == null) return;

            var file = await this.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Save Processed Image",
                    SuggestedFileName = "processed.png",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } }
                    }
                });

            if (file == null) return;

            using var bmp = await ByteArrayToBitmap(_processedGray);
            await using var stream = await file.OpenWriteAsync();
            bmp.Save(stream);
        }

        // Apply Selected Operation
        private async void OnApply(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_loadedColorPixels == null) return;

            // Grayscale conversion happens here on every Apply so every
            // operation always starts from the original loaded image, never chained
            // from a previous Apply's result.
            _processedGray = convertToGrayscale(_loadedColorPixels);

            StatusText.Text = ""; // reset here, otherwise cases that don't set their own message
                                   // would append onto leftover text from a previous Apply click
                                   // instead of starting fresh each time

            var selected = (OperationBox.SelectedItem as string);

            switch (selected)
            {
                case "ConvertToGrayscale":
                    // Already fully working; _processedGray already holds the grayscale
                    // conversion result at this point (computed above, before this switch),
                    // so nothing further is needed here.
                    break;
                case "InvertImage":
                    _processedGray = invertImage(_processedGray);
                    break;
                case "AdjustContrast":
                    _processedGray = adjustContrast(_processedGray);
                    break;
                case "ConvolveImage":
                    _processedGray = convolveImage(_processedGray, createGaussianFilter(5, 1.0f));
                    break;
                case "MedianFilter":
                    _processedGray = medianFilter(_processedGray, 5);
                    break;
                case "EdgeMagnitude":
                    sbyte[,] horizontalKernel = null;                       // Define this kernel yourself
                    sbyte[,] verticalKernel = null;                         // Define this kernel yourself
                    _processedGray = edgeMagnitude(_processedGray, horizontalKernel, verticalKernel);
                    break;
                case "ThresholdImage":
                    _processedGray = thresholdImage(_processedGray, threshold);
                    break;

                case "BinaryErodeImage":
                    bool[,] structElem = null; // Define this structuring element yourself
                    _processedGray = binaryErodeImage(_processedGray, structElem);
                    break;

                case "BinaryDilateImage":
                    structElem = null;
                    _processedGray = binaryDilateImage(_processedGray, structElem);
                    break;

                case "BinaryOpenImage":
                    structElem = null;
                    _processedGray = binaryOpenImage(_processedGray, structElem);
                    break;

                case "BinaryCloseImage":
                    structElem = null;
                    _processedGray = binaryCloseImage(_processedGray, structElem);
                    break;

                case "GrayscaleErodeImage":
                    int[,] grayStructElem = null; // Define this structuring element yourself
                    _processedGray = grayscaleErodeImage(_processedGray, grayStructElem);
                    break;

                case "GrayscaleDilateImage":
                    grayStructElem = null;
                    _processedGray = grayscaleDilateImage(_processedGray, grayStructElem);
                    break;

                default:
                    break;
            }

            ProcessedImage.Source = await ByteArrayToBitmap(_processedGray);
        }

        // ====================================================================
        // ==================== GIVEN (already implemented) ==================
        // ====================================================================

        // Converts the loaded color pixel data ([x, y, channel], channel 0=R,1=G,2=B)
        // to single-channel grayscale. Called from OnApply, not from OnLoadImage, so
        // the color image stays visible in the GUI immediately after loading, and
        // grayscale conversion only happens once an operation is actually run.
        private byte[,] convertToGrayscale(byte[,,] colorPixels)
        {
            int w = colorPixels.GetLength(0);
            int h = colorPixels.GetLength(1);
            byte[,] gray = new byte[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int r = colorPixels[x, y, 0];
                    int g = colorPixels[x, y, 1];
                    int b = colorPixels[x, y, 2];
                    gray[x, y] = (byte)((r + g + b) / 3);
                }
            return gray;
        }

        // ====================================================================
        // ==================== FUNCTIONS TO IMPLEMENT =======================
        // ====================================================================

        private byte[,] invertImage(byte[,] inputImage)
        {
            // create temporary grayscale image
            byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

            // TODO: add your functionality and checks

            return tempImage;
        }

        private byte[,] adjustContrast(byte[,] inputImage)
        {
            // create temporary grayscale image
            byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

            // TODO: add your functionality and checks

            return tempImage;
        }

        private float[,] createGaussianFilter(byte size, float sigma)
        {
            // create the filter
            float[,] filter = new float[size, size];

            // TODO: add your functionality and checks

            return filter;
        }

        private byte[,] convolveImage(byte[,] inputImage, float[,] filter)
        {
            // create temporary grayscale image
            byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

            // TODO: add your functionality and checks, think about border handling and type conversion

            return tempImage;
        }

        private byte[,] medianFilter(byte[,] inputImage, byte kernelSize)
        {
            // create temporary grayscale image
            byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

            // TODO: add your functionality and checks, think about border handling

            return tempImage;
        }

        private byte[,] edgeMagnitude(byte[,] inputImage, sbyte[,] horizontalKernel, sbyte[,] verticalKernel)
        {
            // create temporary grayscale image
            byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

            // TODO: add your functionality and checks, think about border handling and type conversion (negative values!)

            return tempImage;
        }

        private byte[,] thresholdImage(byte[,] inputImage, byte threshold)
        {
            // create temporary grayscale image
            byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

            // TODO: add your functionality and checks, think about how to represent the binary values

            return tempImage;
        }

        private byte[,] binaryErodeImage(byte[,] inputImage, bool[,] structElem)
        {
            byte[,] output = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
            // TODO: implement binary erosion
            return output;
        }

        private byte[,] binaryDilateImage(byte[,] inputImage, bool[,] structElem)
        {
            byte[,] output = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
            // TODO: implement binary dilation
            return output;
        }

        private byte[,] binaryOpenImage(byte[,] inputImage, bool[,] structElem)
        {
            byte[,] output = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
            // TODO: implement binary opening
            return output;
        }

        private byte[,] binaryCloseImage(byte[,] inputImage, bool[,] structElem)
        {
            byte[,] output = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
            // TODO: implement binary closing
            return output;
        }

        private byte[,] grayscaleErodeImage(byte[,] inputImage, int[,] structElem)
        {
            byte[,] output = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
            // TODO: implement grayscale erosion
            return output;
        }

        private byte[,] grayscaleDilateImage(byte[,] inputImage, int[,] structElem)
        {
            byte[,] output = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
            // TODO: implement grayscale dilation
            return output;
        }

        // ====================================================================
        // ==================== IMAGE <-> BITMAP HELPERS (given) =============
        // ====================================================================

        // Builds a displayable/savable WriteableBitmap from a grayscale byte[,] array
        // (replicated into R, G, B; alpha fully opaque), using only Avalonia's own
        // imaging APIs; no external image library.
        private Task<WriteableBitmap> ByteArrayToBitmap(byte[,] gray)
        {
            int w = gray.GetLength(0);
            int h = gray.GetLength(1);
            var size = new PixelSize(w, h);
            var bmp = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);

            using (var fb = bmp.Lock())
            {
                int totalBytes = fb.RowBytes * h;
                byte[] buffer = new byte[totalBytes];
                for (int y = 0; y < h; y++)
                {
                    int rowStart = y * fb.RowBytes;
                    for (int x = 0; x < w; x++)
                    {
                        byte val = gray[x, y];
                        int idx = rowStart + x * 4;
                        buffer[idx + 0] = val; // R
                        buffer[idx + 1] = val; // G
                        buffer[idx + 2] = val; // B
                        buffer[idx + 3] = 255; // A (fully opaque)
                    }
                }
                Marshal.Copy(buffer, 0, fb.Address, totalBytes);
            }

            return Task.FromResult(bmp);
        }
    }
}
