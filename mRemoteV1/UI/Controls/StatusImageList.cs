using System;
using System.Drawing;
using System.Windows.Forms;
using mRemoteNG.App;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.MultiUser;
using mRemoteNG.Tree.Root;


namespace mRemoteNG.UI.Controls
{
    public class StatusImageList : IDisposable
    {
        public ImageList ImageList { get; }

        public StatusImageList()
        {
            ImageList = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit,
                ImageSize = new Size(16, 16),
                TransparentColor = Color.Transparent
            };

            FillImageList(ImageList);
        }

        public object ImageGetter(object rowObject)
        {
            return GetKey(rowObject as ConnectionInfo);
        }

        public Image GetImage(ConnectionInfo connectionInfo)
        {
            var key = GetKey(connectionInfo);
            return ImageList.Images.ContainsKey(key)
                ? ImageList.Images[key]
                : null;
        }

        public string GetKey(ConnectionInfo connectionInfo)
        {
            if (connectionInfo == null) return "";
            if (connectionInfo is RootPuttySessionsNodeInfo) return "PuttySessions";
            if (connectionInfo is RootNodeInfo) return "Root";
            if (connectionInfo is ContainerInfo) return "Folder";
            
            return GetConnectionIcon(connectionInfo);
        }

        private static string BuildConnectionIconName(string icon, bool connected, bool inUseByOthers = false)
        {
            var status = connected ? "Play" : "Default";
            return inUseByOthers
                ? $"Connection_{icon}_{status}_InUse"
                : $"Connection_{icon}_{status}";
        }

        private const string DefaultConnectionIcon = "";

        private string GetConnectionIcon(ConnectionInfo connection)
        {
            if (string.IsNullOrEmpty(connection.Icon))
            {
                return DefaultConnectionIcon;
            }

            var connected = connection.OpenConnections.Count > 0;
            var inUseByOthers = ActiveSessionTracker.Instance.IsInUseByOthers(connection);
            var name = BuildConnectionIconName(connection.Icon, connected, inUseByOthers);
            if (!ImageList.Images.ContainsKey(name))
            {
                var image = ConnectionIcon.FromString(connection.Icon);
                if (image == null)
                {
                    return DefaultConnectionIcon;
                }

                var defaultImage = image.ToBitmap();
                var connectedImage = Overlay(image, Resources.ConnectedOverlay);
                AddIfMissing(BuildConnectionIconName(connection.Icon, false), defaultImage);
                AddIfMissing(BuildConnectionIconName(connection.Icon, true), connectedImage);
                AddIfMissing(BuildConnectionIconName(connection.Icon, false, true), InUseOverlay(defaultImage));
                AddIfMissing(BuildConnectionIconName(connection.Icon, true, true), InUseOverlay(connectedImage));
            }
            return name;
        }

        private void AddIfMissing(string key, Image image)
        {
            if (!ImageList.Images.ContainsKey(key))
                ImageList.Images.Add(key, image);
        }

        /// <summary>
        /// Adds a red dot in the top right corner: someone else is connected to this server.
        /// </summary>
        private static Bitmap InUseOverlay(Image background)
        {
            var result = new Bitmap(background);
            using (var gr = Graphics.FromImage(result))
            {
                gr.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var dot = new Rectangle(result.Width - 8, 0, 7, 7);
                gr.FillEllipse(Brushes.White, Rectangle.Inflate(dot, 1, 1));
                gr.FillEllipse(Brushes.Red, dot);
            }
            return result;
        }

        private static Bitmap Overlay(Icon background, Image foreground)
        {
            var result = background.ToBitmap();
            using (var gr = Graphics.FromImage(result))
            {
                gr.DrawImage(foreground, new Rectangle(0, 0, foreground.Width, foreground.Height));
            }
            return result;
        }

        private static void FillImageList(ImageList imageList)
        {
            try
            {
                imageList.Images.Add("Root", Resources.Root);
                imageList.Images.Add("Folder", Resources.Folder);
                imageList.Images.Add("PuttySessions", Resources.PuttySessions);
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace($"Unable to fill the image list of type {nameof(StatusImageList)}", ex);
            }
        }

        public void Dispose()
        {
            ImageList?.Dispose();
        }
    }
}