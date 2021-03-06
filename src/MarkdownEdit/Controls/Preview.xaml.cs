﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using CommonMark;
using HtmlAgilityPack;
using mshtml;
using MarkdownEdit.MarkdownConverters;
using MarkdownEdit.Models;

namespace MarkdownEdit.Controls
{
    public partial class Preview : INotifyPropertyChanged
    {
        public readonly Action<Editor> UpdatePreview;
        private FileSystemWatcher _templateWatcher;
        private int _wordCount;

        public Preview()
        {
            InitializeComponent();
            Browser.Navigate(UserTemplate.Load());
            Unloaded += (sender, args) => _templateWatcher?.Dispose();
            Browser.Navigating += BrowserOnNavigating;
            Browser.PreviewKeyDown += BrowserPreviewKeyDown;
            UpdatePreview = Utility.Debounce<Editor>(editor => Dispatcher.InvokeAsync(() => Update(editor.Text)));

            Task.Factory.StartNew(() =>
            {
                _templateWatcher = Utility.WatchFile(UserTemplate.TemplateFile, () => Dispatcher.Invoke(UpdateTemplate));

                // kill popups
                dynamic activeX = Browser.GetType().InvokeMember("ActiveXInstance",
                    BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
                    null, Browser, new object[] {});

                activeX.Silent = true;
            });
        }

        private void Update(string markdown)
        {
            if (markdown == null) return;
            try
            {
                markdown = Utility.RemoveYamlFrontMatter(markdown);
                var html = MarkdownConverter.ConvertToHtml(markdown);
                var div = GetContentsDiv();
                div.innerHTML = ScrubHtml(html);
                WordCount = div.innerText.WordCount();
            }
            catch (CommonMarkException e)
            {
                MessageBox.Show(e.ToString(), App.Title);
            }
        }

        private static string ScrubHtml(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            Action<HtmlNodeCollection, Action<HtmlNode>> each = (nodes, action) =>
            {
                if (nodes == null) return;
                foreach (var node in nodes) action.Invoke(node);
            };            
            
            // Remove potentially harmful elements
            var nc = doc.DocumentNode.SelectNodes("//script|//link|//iframe|//frameset|//frame|//applet|//object|//embed");
            each(nc, node => node.ParentNode.RemoveChild(node, false));

            // Remove hrefs to java/j/vbscript URLs
            nc = doc.DocumentNode.SelectNodes("//a[starts-with(translate(@href, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'javascript')]|//a[starts-with(translate(@href, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'jscript')]|//a[starts-with(translate(@href, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'vbscript')]");
            each(nc, node => node.SetAttributeValue("href", "#"));

            // Remove img with refs to java/j/vbscript URLs
            nc = doc.DocumentNode.SelectNodes("//img[starts-with(translate(@src, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'javascript')]|//img[starts-with(translate(@src, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'jscript')]|//img[starts-with(translate(@src, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'vbscript')]");
            each(nc, node => node.SetAttributeValue("src", "#"));

            // Remove on<Event> handlers from all tags
            nc = doc.DocumentNode.SelectNodes("//*[@onclick or @onmouseover or @onfocus or @onblur or @onmouseout or @ondoubleclick or @onload or @onunload]");
            each(nc, node =>
            {
                node.Attributes.Remove("onFocus");
                node.Attributes.Remove("onBlur");
                node.Attributes.Remove("onClick");
                node.Attributes.Remove("onMouseOver");
                node.Attributes.Remove("onMouseOut");
                node.Attributes.Remove("onDoubleClick");
                node.Attributes.Remove("onLoad");
                node.Attributes.Remove("onUnload");
            });

            // remove any style attributes that contain the word expression (IE evaluates this as script)
            nc = doc.DocumentNode.SelectNodes("//*[contains(translate(@style, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'expression')]");
            each(nc, node => node.Attributes.Remove("style"));

            return doc.DocumentNode.WriteTo();
        }

        private IHTMLElement GetContentsDiv()
        {
            var document = (IHTMLDocument3)Browser.Document;
            var element = document?.getElementById("content");
            return element;
        }

        private void UpdateTemplate()
        {
            Browser.Refresh();
            MainWindow.UpdatePreviewCommand.Execute(null, this);
        }

        private static void BrowserOnNavigating(object sender, NavigatingCancelEventArgs ea)
        {
            ea.Cancel = true;
            var url = ea.Uri?.ToString();
            if (url?.StartsWith("about:", StringComparison.OrdinalIgnoreCase) == false) Process.Start(url);
        }

        public void SetScrollOffset(ScrollChangedEventArgs ea)
        {
            if (App.UserSettings.SynchronizeScrollPositions == false) return;
            var document2 = (IHTMLDocument2)Browser.Document;
            var document3 = (IHTMLDocument3)Browser.Document;
            if (document3?.documentElement != null)
            {
                var percentToScroll = PercentScroll(ea);
                if (percentToScroll > 0.99) percentToScroll = 1.1; // deal with round off at end of scroll
                var body = document2.body; //document3.getElementsByTagName("body").item(0);
                var scrollHeight = ((IHTMLElement2)body).scrollHeight - document3.documentElement.offsetHeight;
                document2.parentWindow.scroll(0, (int)Math.Ceiling(percentToScroll * scrollHeight));
            }
        }

        private static double PercentScroll(ScrollChangedEventArgs e)
        {
            var y = e.ExtentHeight - e.ViewportHeight;
            return e.VerticalOffset / ((Math.Abs(y) < .000001) ? 1 : y);
        }

        private void BrowserPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.O:
                    if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                    {
                        ApplicationCommands.Open.Execute(this, Application.Current.MainWindow);
                        e.Handled = true;
                    }
                    break;

                case Key.N:
                    if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                    {
                        ApplicationCommands.New.Execute(this, Application.Current.MainWindow);
                        e.Handled = true;
                    }
                    break;

                case Key.F1:
                    ApplicationCommands.Help.Execute(this, Application.Current.MainWindow);
                    e.Handled = true;
                    break;

                case Key.F5:
                    e.Handled = true;
                    break;
            }
        }

        // Properties

        public int WordCount
        {
            get { return _wordCount; }
            set { Set(ref _wordCount, value); }
        }

        public static readonly DependencyProperty MarkdownConverterProperty = DependencyProperty.Register(
            "MarkdownConverter", typeof (IMarkdownConverter), typeof (Preview), new PropertyMetadata(default(IMarkdownConverter)));

        public IMarkdownConverter MarkdownConverter
        {
            get { return (IMarkdownConverter)GetValue(MarkdownConverterProperty); }
            set { SetValue(MarkdownConverterProperty, value); }
        }

        // INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T property, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(property, value) == false)
            {
                property = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}