using HtmlAgilityPack;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace DaumBlogBackupPrettifier {
    public partial class Form1 : Form {
        FolderBrowserDialog dialog;

        TaskDialogPage? taskPage;
        TaskDialogButtonCollection tdbc;

        BackgroundWorker worker;

        struct Article {
            public string hrefPath;
            public string title;
            public string category;
            public string timestr;
            //public string contentText;
        }

        string? baseDir;
        string[]? folders;
        string[]? folderNames;

        public Form1() {
            InitializeComponent();

            dialog = new ();
            tdbc = new TaskDialogButtonCollection();
            tdbc.Add(new TaskDialogButton("취소"));

            worker = new BackgroundWorker();
            worker.DoWork += Work;
        }

        private void button1_Click(object sender, EventArgs e) {
            taskPage = new TaskDialogPage() {
                Buttons = tdbc,
                Caption = "다음 블로그 백업 정리기",
                Heading = "준비 중 (1/3)",
                Text = "글 목록을 확인하고 있습니다",
                ProgressBar = new TaskDialogProgressBar(TaskDialogProgressBarState.Marquee)
            };

            worker.RunWorkerAsync();

            TaskDialog.ShowDialog(taskPage!);

            TaskDialogButtonCollection tdbcDone = new();
            tdbcDone.Add(new TaskDialogButton("예") { Tag = DialogResult.Yes });
            tdbcDone.Add(new TaskDialogButton("아니오") { Tag = DialogResult.No });

            DialogResult shouldOpenIndex = (DialogResult)(TaskDialog.ShowDialog(new TaskDialogPage() {
                Buttons = tdbcDone,
                Caption = "다음 블로그 백업 정리기",
                Heading = "작업이 완료되었습니다",
                Text = "새로 생성된 index.html 파일을 열면 백업된 글들을 쉽게 모아볼 수 있습니다.\n\n지금 바로 확인하시겠습니까?"
            }).Tag!);

            if (shouldOpenIndex == DialogResult.Yes) {
                Process.Start(new ProcessStartInfo() {
                    FileName = Path.Combine(baseDir!, "index.html"),
                    UseShellExecute = true
                });
            }
        }

        private void UpdateTaskPageHeading(string? text) {
            this.Invoke(new Action(() => { taskPage!.Heading = text; }));
        }

        private void UpdateTaskPageText(string? text) {
            this.Invoke(new Action(() => { taskPage!.Text = text; }));
        }

        private void SetTaskPageProgress(bool marquee, int value, int max) {
            if (taskPage == null || taskPage.ProgressBar == null) return;
            this.Invoke(new Action(() => { 
                if (marquee) {
                    taskPage!.ProgressBar!.State = TaskDialogProgressBarState.Marquee;
                } else {
                    taskPage!.ProgressBar!.State = TaskDialogProgressBarState.Normal;
                    taskPage!.ProgressBar!.Maximum = max;
                    taskPage!.ProgressBar!.Value = value;
                }
            }));
        }

        private void CloseTaskDialog() {
            this.Invoke(new Action(() => { if (taskPage != null && taskPage.BoundDialog != null) taskPage.BoundDialog.Close(); }));
        }

        private void Work(object? sender, DoWorkEventArgs e) {
            Thread.Sleep(1000);

            List<string> htmlFilesToCheck = new ();

            foreach (var thing in folders!) {
                var files = Directory.GetFiles(thing);
                foreach (var file in files) {
                    if (file.ToLower().EndsWith(".html")) htmlFilesToCheck.Add(file);
                }
            }

            UpdateTaskPageHeading("분석 및 수정 중 (2/3)");

            List<Article> articles = new ();
            for (int i = 0; i < htmlFilesToCheck.Count; i++) {
                string thing = htmlFilesToCheck[i];
                UpdateTaskPageText(thing);
                SetTaskPageProgress(false, i, htmlFilesToCheck.Count);

                var doc = new HtmlDocument();
                doc.Load(thing);

                // redirect cfs*.planet.daum.net images to local copy
                var images = doc.DocumentNode.SelectNodes("//img");
                if (images != null) {
                    int unknownImagesCount = 0;
                    foreach (var img in images) {
                        var imgSrc = img.Attributes["src"].Value;
                        if (imgSrc.StartsWith("http://cfs") && imgSrc.Contains(".planet.daum.net/upload_control/pcp_download.php")) {
                            var hidx = imgSrc.IndexOf("?fhandle=");
                            var fidx = imgSrc.IndexOf("&filename=");
                            var rawFileHandle = imgSrc.Substring(hidx + 9, fidx - (hidx + 9));
                            var fileHandle = Encoding.UTF8.GetString(Convert.FromBase64String(rawFileHandle));  // 2D5Dz@fs2.planet.daum.net:/471223/0/0.JPG
                            var suggestedFilename = imgSrc.Substring(fidx + 10);                                // 0.JPG
                                                                                                                // real stored filename: 15_35_27_23_2D5Dz_471223_0_0.JPG

                            var realFilenameGuess = fileHandle.Substring(fileHandle.IndexOf(":/") + 2).Replace('/', '_');
                            if (realFilenameGuess.EndsWith(".thumb")) realFilenameGuess = realFilenameGuess.Replace(".thumb", "");

                            var imageDirectory = Path.Combine(Directory.GetParent(thing)!.FullName, "img");
                            var imageFiles = Directory.GetFiles(imageDirectory);
                            string? realFilename = null;
                            foreach (var imageFile in imageFiles) {
                                if (imageFile.EndsWith(realFilenameGuess)) {
                                    // is this real file?
                                    realFilename = Path.GetFileName(imageFile);
                                }
                            }

                            if (realFilename != null) {
                                img.Attributes["src"].Value = "./img/" + realFilename;
                            } else {
                                var unknownImageName = (unknownImagesCount == 0) ? "image.jpg" : "image_" + unknownImagesCount + ".jpg";
                                if (File.Exists(Path.Combine(Directory.GetParent(thing)!.FullName, "img", unknownImageName))) {
                                    // TODO: display warning that image linkage might be wrong
                                    img.Attributes["src"].Value = "./img/" + unknownImageName;
                                    unknownImagesCount++;
                                } else {
                                    throw new Exception("이미지를 찾지 못했습니다 (" + suggestedFilename + ")");
                                }
                            }
                        }
                    }
                }

                // remove non-working scrap origin links
                var scrapLinks = doc.DocumentNode.SelectNodes("//a[contains(@class, 'under')]");
                if (scrapLinks != null) {
                    foreach (var scrapLink in scrapLinks) {
                        if (scrapLink.Attributes["href"].Value == "null") {
                            scrapLink.Remove();
                        }
                    }
                }

                // save changes
                //if (!File.Exists(thing + ".bak")) File.Copy(thing, thing + ".bak");
                doc.Save(thing);

                var article = new Article() {
                    hrefPath = Directory.GetParent(thing)!.Name + "/" + Path.GetFileName(thing),
                    title = doc.DocumentNode.SelectNodes("//h2[contains(@class, 'title-article')]").First().InnerText.Replace("\r", "").Replace("\n", ""),
                    category = doc.DocumentNode.SelectNodes("//p[contains(@class, 'category')]").First().InnerText,
                    timestr = doc.DocumentNode.SelectNodes("//p[contains(@class, 'date')]").First().InnerText
                    //contentText = doc.DocumentNode.SelectNodes("//div[contains(@class, 'article-view')]").First().InnerText.Replace("\r", "").Replace("\n", "").Replace("&nbsp;", "")
                };

                articles.Add(article);
            }

            UpdateTaskPageHeading("완료 중 (3/3)");
            UpdateTaskPageText("메인 페이지를 만드는 중입니다");
            SetTaskPageProgress(true, 0, 0);

            StringBuilder sb = new();
            List<string> categories = new();

            sb.AppendLine("var entries = [");

            foreach (var article in articles) {
                sb.Append("{");
                sb.Append($"\"href_path\":\"{HttpUtility.HtmlEncode(article.hrefPath)}\", \"title\":\"{HttpUtility.HtmlEncode(article.title)}\", \"timestr\":\"{article.timestr}\", \"category\":\"{HttpUtility.HtmlEncode(article.category)}\"");
                //sb.Append($"\"href_path\":\"{article.hrefPath}\", \"title\":\"{HttpUtility.HtmlEncode(article.title)}\", \"timestr\":\"{article.timestr}\", \"category\":\"{HttpUtility.HtmlEncode(article.category)}\", \"content_text\":\"{HttpUtility.HtmlEncode(Regex.Replace(article.contentText, @"\s+", " "))}\"");
                sb.AppendLine("},");
                if (!categories.Contains(article.category)) categories.Add(article.category);
            }

            sb.Length = sb.Length - 1;

            sb.Append("\n];\n");
            
            sb.AppendLine("var categories = [");
            
            foreach (var category in categories) {
                if (category.Trim().Length > 0) sb.Append($"\"{category}\",");
            }

            sb.Append("\n];");

            File.WriteAllText(Path.Combine(baseDir!, "data.js"), sb.ToString());

            File.WriteAllBytes(Path.Combine(baseDir!, "index.html"), Convert.FromBase64String(Index.indexHtml));

            Thread.Sleep(500);

            CloseTaskDialog();
        }

        private void button2_Click(object sender, EventArgs e) {
            button1.Enabled = false;
            if (dialog.ShowDialog() != DialogResult.OK) return;

            if (!File.Exists(Path.Combine(dialog.SelectedPath, "style.css"))) {
                TaskDialog.ShowDialog(new TaskDialogPage() {
                    Caption = "오류",
                    Heading = "잘못된 폴더입니다!",
                    Text = "다음 블로그 백업의 압축을 해제한 폴더를 선택해주세요",
                    Icon = TaskDialogIcon.Error
                });
                button1.Enabled = false;
                return;
            }

            baseDir = dialog.SelectedPath;
            folders = Directory.GetDirectories(baseDir);
            folderNames = new string[folders.Length];
            for (int i = 0; i < folders.Length; i++) folderNames[i] = Path.GetFileName(folders[i])!;

            textBox1.Text = baseDir;
            button1.Enabled = true;
        }
    }
}