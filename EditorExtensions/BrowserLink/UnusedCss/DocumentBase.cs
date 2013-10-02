﻿using Microsoft.CSS.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MadsKristensen.EditorExtensions.BrowserLink.UnusedCss
{
    public abstract class DocumentBase : IDocument
    {
        private readonly string _file;
        private readonly FileSystemWatcher _watcher;
        private readonly string _localFileName;

        protected DocumentBase(string file)
        {
            _file = file;
            var path = Path.GetDirectoryName(file);
            _localFileName = (Path.GetFileName(file) ?? "").ToLowerInvariant();

            _watcher = new FileSystemWatcher
            {
                Path = path,
                Filter = _localFileName,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.LastAccess | NotifyFilters.DirectoryName
            };

            _watcher.Changed += Reparse;
            _watcher.Renamed += ProxyRename;
            _watcher.Created += Reparse;
            _watcher.EnableRaisingEvents = true;
            Reparse();
        }

        public IEnumerable<IStylingRule> Rules { get; private set; }

        public void Dispose()
        {
            _watcher.Changed -= Reparse;
            _watcher.Renamed -= ProxyRename;
            _watcher.Dispose();
        }

        private void ProxyRename(object sender, RenamedEventArgs e)
        {
            if (e.Name.ToLowerInvariant() == _localFileName)
            {
                Reparse();
            }
        }

        public bool SnapshotOnChange { get; set; }

        private async void Reparse(object sender, FileSystemEventArgs e)
        {
            if (e != null && e.Name.ToLowerInvariant() != _localFileName)
            {
                return;
            }

            var tryCount = 0;
            const int maxTries = 20;

            while (tryCount++ < maxTries)
            {
                try
                {
                    var text = File.ReadAllText(_file);
                    Reparse(text);
                    break;
                }
                catch (IOException)
                {
                }
                await Task.Delay(100);
            }

            await UsageRegistry.ResyncAsync();

            if (SnapshotOnChange)
            {
                UnusedCssExtension.All(x => x.SnapshotPage());
            }
        }

        public void Reparse()
        {
            Reparse(null, null);
        }

        protected static IDocument For(string fullPath, bool createIfRequired, Func<string, DocumentBase> documentFactory)
        {
            var fileName = fullPath.ToLowerInvariant();

            if (createIfRequired)
            {
                return documentFactory(fileName);
            }

            return null;
        }

        public void Reparse(string text)
        {
            var parser = GetParser();
            var parseResult = parser.Parse(text, false);
            Rules = new CssItemAggregator<IStylingRule>(true) { (RuleSet rs) => CssRule.From(_file, text, rs, this) }
                        .Crawl(parseResult)
                        .Where(x => x != null)
                        .ToList();
        }

        protected abstract ICssParser GetParser();

        public virtual string GetSelectorName(RuleSet ruleSet)
        {
            return ExtractSelectorName(ruleSet);
        }

        internal static string ExtractSelectorName(RuleSet ruleSet)
        {
            return ruleSet.Text.Substring(0, ruleSet.Block.Start - ruleSet.Start);
        }
    }
}
