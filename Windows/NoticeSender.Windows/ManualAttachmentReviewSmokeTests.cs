using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using NoticeSender.Automation;
using NoticeSender.Core;

namespace NoticeSender.Windows;

internal static class ManualAttachmentReviewSmokeTests
{
    internal static void Run()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "synthetic review UI only");
            var batch = new BatchItem { ChatRoomName = "합성 확인 화면", AttachmentPaths = [path] };
            var manifest = ReviewedAttachmentBatch.Capture([batch]);
            VerifyLayout(batch.ChatRoomName, manifest.Items[0], minimum: false);
            VerifyLayout(batch.ChatRoomName, manifest.Items[0], minimum: true);
            var longRoom = new BatchItem { ChatRoomName = "합성 확인 화면 · " + new string('가', 160), AttachmentPaths = [path] };
            var longManifest = ReviewedAttachmentBatch.Capture([longRoom]);
            VerifyLayout(longRoom.ChatRoomName, longManifest.Items[0], minimum: false);
            VerifyLayout(longRoom.ChatRoomName, longManifest.Items[0], minimum: true);
            var stop = new ManualAttachmentReviewWindow(batch.ChatRoomName, manifest.Items[0]);
            stop.Loaded += (_, _) => stop.Dispatcher.BeginInvoke(new Action(() =>
            {
                Require(stop.Acknowledgement.IsChecked != true && !stop.ConfirmButton.IsEnabled && !stop.ConfirmButton.IsDefault, "Manual confirmation was preselected or default accepted.");
                stop.ConfirmButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Require(stop.IsVisible, "Unchecked manual confirmation accepted.");
                stop.StopButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }));
            Require(stop.ShowDialog() != true, "Declined manual review accepted.");
            var accept = new ManualAttachmentReviewWindow(batch.ChatRoomName, manifest.Items[0]);
            accept.Loaded += (_, _) => accept.Dispatcher.BeginInvoke(new Action(() =>
            {
                accept.Acknowledgement.IsChecked = true;
                Require(accept.ConfirmButton.IsEnabled, "Explicit acknowledgement did not enable confirmation.");
                accept.ConfirmButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }));
            Require(accept.ShowDialog() == true, "Explicit manual confirmation was lost.");
        }
        finally { File.Delete(path); }
    }
    private static void VerifyLayout(string room, ReviewedAttachmentItem item, bool minimum)
    {
        var window = new ManualAttachmentReviewWindow(room, item);
        if (minimum) { window.Width = window.MinWidth; window.Height = window.MinHeight; }
        Exception? failure = null;
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                window.UpdateLayout();
                var client = (FrameworkElement)window.Content;
                var bounds = new Rect(0, 0, client.ActualWidth, client.ActualHeight);
                foreach (FrameworkElement control in new FrameworkElement[] { window.Acknowledgement, window.StopButton, window.ConfirmButton })
                {
                    Rect rendered = control.TransformToAncestor(client).TransformBounds(new Rect(control.RenderSize));
                    Require(control.ActualWidth > 0 && control.ActualHeight > 0 && rendered.Left >= -.5 && rendered.Top >= -.5 &&
                        rendered.Right <= bounds.Right + .5 && rendered.Bottom <= bounds.Bottom + .5,
                        $"Manual review control is clipped at {window.Width}x{window.Height}: {control.GetType().Name}, rendered {rendered}, client {bounds}.");
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { window.DialogResult = false; }
        }));
        window.ShowDialog(); if (failure != null) throw failure;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
