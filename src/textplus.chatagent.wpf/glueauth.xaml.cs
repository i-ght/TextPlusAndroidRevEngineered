using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace textplus.chatagent.wpf
{
    public partial class glueauth : Window
    {
        public static string SSL_VERIFY_HOST = "www.gluecondom.com";
        public static string POST_PROXY = "";
        public static string APPLICATION_NAME = "appnamehere";
        public static string APPLICATION_VERSION = "appversionhere";
        public string[] APPLICATION_ARGUMENTS = new string[0];
        public const string APPLICATION_UPDATER_PATH = "http://www.gluezone.net/gcondom/Apps/updater.exe";
        public const string HATESNOW_BLACKLIST = "http://www.pinkgirlpics.com/hatesn0w/get.php";
        public static List<string> lstYANIVBLACKLIST = new List<string>();

        public static gProtection condom = new gProtection();
        private LStatus status = LStatus.None;
        private bool openone = false;
        private Window mainf = null;

        enum LStatus
        {
            None,
            Success,
            Failed,
            Closing
        }

        public glueauth(Window MAIN_FORM, string APP_NAME, string APP_VERSION, string[] ARGS)
        {
            InitializeComponent();
            condom.Dispatcher = Dispatcher;
            ServicePointManager.Expect100Continue = false;
            //ServicePointManager.DefaultConnectionLimit = 100;
            this.mainf = MAIN_FORM;
            APPLICATION_NAME = APP_NAME;
            APPLICATION_VERSION = APP_VERSION;
            this.APPLICATION_ARGUMENTS = ARGS;
        }

        private void changeStatus(string data)
        {
            Dispatcher.Invoke(delegate
            {
                this.LblStatus.Content = data;
            });
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            Username = TxtUsername.Text;
            Password = TxtPassword.Text;

            new Thread(validate).Start();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try {
                if (Registry.CurrentUser.OpenSubKey("Software\\GAuth") != null) {
                    RegistryKey ky = Registry.CurrentUser.OpenSubKey("Software\\GAuth", RegistryKeyPermissionCheck.ReadWriteSubTree, System.Security.AccessControl.RegistryRights.ReadKey);
                    if (ky.GetValue("user") != null && ky.GetValue("pass") != null) {
                        this.TxtUsername.Text = ky.GetValue("user").ToString();
                        this.TxtPassword.Text = ky.GetValue("pass").ToString();
                    }
                    ky.Close();
                    if (!string.IsNullOrEmpty(this.TxtUsername.Text) && !string.IsNullOrEmpty(this.TxtPassword.Text) && APPLICATION_ARGUMENTS.Length > 0) {
                        Username = TxtUsername.Text;
                        Password = TxtPassword.Text;

                        new Thread(validate).Start();
                    }
                }
            }
            catch { }
        }

        private void validate()
        {

            this.status = LStatus.None;
            ThreadPool.QueueUserWorkItem(new WaitCallback(this.vT));
            while (this.status == LStatus.None) {
                Thread.Sleep(10);
            }
            if (this.status == LStatus.Success && !(openone)) {
                openone = true;
                try {
                    if (Registry.CurrentUser.OpenSubKey("Software\\GAuth") == null) {
                        Registry.CurrentUser.CreateSubKey("Software\\GAuth");
                    }
                    RegistryKey ky = Registry.CurrentUser.OpenSubKey("Software\\GAuth", RegistryKeyPermissionCheck.ReadWriteSubTree, System.Security.AccessControl.RegistryRights.FullControl);
                    ky.SetValue("user", Username);
                    ky.SetValue("pass", Password);
                    ky.Close();
                }
                catch { }
                if (this.mainf != null) {
                    Dispatcher.Invoke(delegate
                    {
                        mainf.Show();
                        this.Hide();
                        this.ShowInTaskbar = false;
                    });
                }
            }
        }

        public static string Username { get; set; }
        protected string Password { get; set; }

        private void vT(object obj)
        {
            changeStatus("Logging In...");
            int lIN = condom.loginCallback(Username, Password);
            switch (lIN) {
                case 0:
                    changeStatus("Failed...");
                    break;
                case 1:
                    changeStatus("Socket connect failure! Please retry!");
                    break;
                case 3:
                    changeStatus("Invalid Credentials!");
                    break;
                case 4:
                    changeStatus("Unknown Error!");
                    break;
                case 5:
                    changeStatus("Success!");
                    Thread.Sleep(500);
                    changeStatus("Preparing...");
                    if (condom.SetOnline(APPLICATION_NAME, Username)) {
                        changeStatus("Getting required data..");
                        if (condom.GetYANIVBLACKLIST()) {
                            changeStatus("Checking for Updates..");
                            string up = condom.checkForUpdates(APPLICATION_NAME, APPLICATION_VERSION);
                            if (!string.IsNullOrEmpty(up)) {
                                if (MsgBox("An update has been found for this program!\r\n\r\n Would you like to download it?\r\n\r\nWARNING: You need to have EVERY open instace of this program closed before updating, or else it'll fail!", "Update found!", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) {
                                    try {
                                        changeStatus("Downloading Update...");
                                        WebClient wc = new WebClient();
                                        if (!File.Exists(System.AppDomain.CurrentDomain.BaseDirectory + "\\updater.exe")) {
                                            wc.DownloadFile(APPLICATION_UPDATER_PATH, System.AppDomain.CurrentDomain.BaseDirectory + "\\updater.exe");
                                            Thread.Sleep(450);
                                        }
                                        if (File.Exists(System.AppDomain.CurrentDomain.BaseDirectory + "\\updater.exe")) {
                                            string target = System.AppDomain.CurrentDomain.BaseDirectory + "\\temp." + System.AppDomain.CurrentDomain.FriendlyName;
                                            wc.DownloadFile(up, target);
                                            Thread.Sleep(450);
                                            if (File.Exists(target)) {
                                                changeStatus("Program will now apply update!");
                                                Thread.Sleep(500);
                                                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                                                ProcessStartInfo pi = new ProcessStartInfo();
                                                pi.FileName = System.AppDomain.CurrentDomain.BaseDirectory + "\\updater.exe";
                                                pi.Arguments = HttpUtility.UrlEncode(target) + " " + System.Web.HttpUtility.UrlEncode(System.AppDomain.CurrentDomain.BaseDirectory + "\\" + System.AppDomain.CurrentDomain.FriendlyName) + " " + pid.ToString();
                                                Process.Start(pi);
                                                Process.GetCurrentProcess().Kill();
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                            this.status = LStatus.Success;
                        }
                    } else {
                        changeStatus("You don't have access to this program!");
                        this.status = LStatus.Failed;
                    }
                    break;
            }
        }

        public MessageBoxResult MsgBox(string s1, string s2, MessageBoxButton btn, MessageBoxImage img)
        {
            //
            //Update FOUND!
            return Dispatcher.Invoke(() => MessageBox.Show(s1, s2, btn, img));
        }

        //private void TxtPassword_KeyDown(object sender, KeyEventArgs e)
        //{
        //    if (e.KeyCode == Keys.Enter)
        //    {
        //        validate();
        //    }
        //}


        private void glue_auth_Closing(object sender, CancelEventArgs e)
        {
            Process.GetCurrentProcess().Kill();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            Process.GetCurrentProcess().Kill();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            this.status = LStatus.Closing;
            Process.GetCurrentProcess().Kill();
        }
    }

    public class DESclass
    {
        public Dispatcher Dispatcher { get; set; }

        public string Encrypt(string public_key, string data_to_encrypt)
        {
            byte[] key = Encoding.ASCII.GetBytes(public_key);
            byte[] iv = Encoding.ASCII.GetBytes("password");
            byte[] data = Encoding.ASCII.GetBytes(data_to_encrypt);
            byte[] enc = new byte[0];
            TripleDES tdes = TripleDES.Create();
            tdes.IV = iv;
            tdes.Key = key;
            tdes.Mode = CipherMode.CBC;
            tdes.Padding = PaddingMode.Zeros;
            ICryptoTransform ict = tdes.CreateEncryptor();
            enc = ict.TransformFinalBlock(data, 0, data.Length);
            string final = Bin2Hex(enc);
            return final;
        }


        static string Bin2Hex(byte[] bin)
        {
            StringBuilder sb = new StringBuilder(bin.Length * 2);
            foreach (byte b in bin) {
                sb.Append(b.ToString("x").PadLeft(2, '0'));
            }
            return sb.ToString();
        }
    }

    public class gProtection
    {
        private string user, session, appname;
        private const string URL = "https://www.gluecondom.com/rayn/call.php";
        public Dispatcher Dispatcher { get; set; }

        public int loginCallback(string user, string pass)
        {
            string systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string hostsPath = @"drivers\etc\hosts";
            string content = File.ReadAllText(Path.Combine(systemPath, hostsPath)).ToLower();
            if (content.Contains("gluezone.net") || content.Contains("gluecondom")) { return 0; }
            beg:
            string src = Post(URL, null, null, "action=start&pstart=pstart", null, null, 60, true);
            if (string.IsNullOrEmpty(src)) { return 1; }
            if (!Regex.IsMatch(src, "id=\"result\">([^<]*)</div>")) { return 1; }
            string key = HttpUtility.HtmlDecode(Regex.Match(src, "id=\"result\">([^<]*)</div>").Groups[1].ToString());
            if (key == null) { return 1; }
            if (key.Length < 24) {
                goto beg;
            }
            string sPost = "action=login&user=" + HttpUtility.UrlEncode(user) + "&pass=" + HttpUtility.UrlEncode(pass) + "&key=" + key;
            src = Post(URL, null, null, sPost, null, null, 60, true);
            if (string.IsNullOrEmpty(src)) { return 1; }
            if (!Regex.IsMatch(src, "id=\"result\">([^<]*)</div>")) { return 1; }
            string response = HttpUtility.HtmlDecode(Regex.Match(src, "id=\"result\">([^<]*)</div>").Groups[1].ToString());
            if (string.IsNullOrEmpty(response)) {
                return 3;
            }
            //string valid = md5Hex(user);
            DESclass tdes = new DESclass { Dispatcher = Dispatcher };

            string valid = tdes.Encrypt(key, user);
            if (user.Length == 8 && response.Length == 32 && response.Substring(0, 16) == valid) {
                return 5;
            }
            if (response != valid) {
                if (response == md5Hex("INVALID")) {
                    return 3;
                } else {
                    return 4;
                }
            }
            return 5;
        }

        public bool SetOnline(string appname, string user)
        {
            string systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string hostsPath = @"drivers\etc\hosts";
            string content = File.ReadAllText(Path.Combine(systemPath, hostsPath)).ToLower();
            if (content.Contains("gluezone.net") || content.Contains("gluecondom")) { return false; }
            this.appname = appname;
            string sPost = "action=createsession&user=" + HttpUtility.UrlEncode(user) + "&app=" + HttpUtility.UrlEncode(appname) + "&appversion=" + HttpUtility.UrlEncode(glueauth.APPLICATION_VERSION);
            string src = Post(URL, null, null, sPost, null, null, 60, true);
            if (string.IsNullOrEmpty(src)) { return false; }
            if (!Regex.IsMatch(src, "id=\"result\">([^<]*)</div>")) { return false; }
            string response = HttpUtility.HtmlDecode(Regex.Match(src, "id=\"result\">([^<]*)</div>").Groups[1].ToString());
            if (response.Contains("FAILED")) {
                if (response.Contains("MUST UPDATE-")) {
                    string update_path = response.Split(new string[] { "MUST UPDATE-" }, StringSplitOptions.None)[1];
                    if (string.IsNullOrEmpty(update_path))
                        return false;

                    if (MsgBox("An update has been found for this program!\r\n\r\nYou MUST download it...\r\n\r\nWARNING: You need to have EVERY open instace of this program closed before updating, or else it'll fail!", "Update FOUND!", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK) {
                        try {
                            WebClient wc = new WebClient();
                            if (!File.Exists(System.AppDomain.CurrentDomain.BaseDirectory + "\\updater.exe")) {
                                wc.DownloadFile(glueauth.APPLICATION_UPDATER_PATH, System.AppDomain.CurrentDomain.BaseDirectory + "\\updater.exe");
                                Thread.Sleep(450);
                            }

                            if (File.Exists(System.AppDomain.CurrentDomain.BaseDirectory + "\\updater.exe")) {
                                string target = System.AppDomain.CurrentDomain.BaseDirectory + "\\temp." + System.AppDomain.CurrentDomain.FriendlyName;
                                wc.DownloadFile(update_path, target);
                                Thread.Sleep(450);
                                if (File.Exists(target)) {
                                    Thread.Sleep(500);
                                    int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                                    ProcessStartInfo pi = new ProcessStartInfo();
                                    pi.FileName = System.AppDomain.CurrentDomain.BaseDirectory + "\\updater.exe";
                                    pi.Arguments = System.Web.HttpUtility.UrlEncode(target) + " " + System.Web.HttpUtility.UrlEncode(System.AppDomain.CurrentDomain.BaseDirectory + "\\" + System.AppDomain.CurrentDomain.FriendlyName) + " " + pid.ToString();
                                    Process.Start(pi);
                                    Application.Current.Shutdown();
                                } else
                                    return false;
                            } else
                                return false;
                        }
                        catch {
                            return false;
                        }
                    } else
                        return false;
                } else
                    return false;
            }
            this.user = user;
            this.session = response.Trim();
            Thread t = new Thread(new ThreadStart(this.Pinger));
            t.IsBackground = true;
            t.Start();
            return true;
        }


        public MessageBoxResult MsgBox(string s1, string s2, MessageBoxButton btn, MessageBoxImage img)
        {
            //
            //Update FOUND!
            return Dispatcher.Invoke(() => MessageBox.Show(s1, s2, btn, img));
        }

        public void SubmitLink(string link)
        {
            string postdata = "action=submitlink&user=" + HttpUtility.UrlEncode(this.user) + "&app=" + HttpUtility.UrlEncode(appname) + "&link=" + HttpUtility.UrlEncode(link);
            Post(URL.Replace("https://", "http://"), null, null, postdata, null, null, 15, false);
        }

        public bool GetYANIVBLACKLIST()
        {
            string src = this.Get2(glueauth.HATESNOW_BLACKLIST, null, null, null, false, true);
            try {
                if (string.IsNullOrEmpty(src)) return false;
                src = src.Split(new string[] { "[ENDHEADERS]" }, StringSplitOptions.None)[1];
                if (string.IsNullOrEmpty(src)) return false;
                if (src.Contains("FAILED\r\n")) return false;
                foreach (string s in src.Split(new string[] { "\n" }, StringSplitOptions.RemoveEmptyEntries))
                    if (!string.IsNullOrEmpty(s) || string.IsNullOrEmpty(s.Trim()))
                        glueauth.lstYANIVBLACKLIST.Add(s.Trim());
            }
            catch { return false; }

            return true;
        }

        private void Pinger()
        {
            while (true) {
                if (!string.IsNullOrEmpty(this.user) && !string.IsNullOrEmpty(this.session)) {
                    string sPost = "action=ping&session=" + HttpUtility.UrlEncode(this.session);
                    Post(URL.Replace("https://", "http://"), null, null, sPost, null, null, 120, true);
                }
                Thread.Sleep(60000);
            }
        }

        private string strrev(string data)
        {
            char[] arr = data.ToCharArray();
            Array.Reverse(arr);
            return new string(arr);
        }

        public string checkForUpdates(string aN, string aV)
        {
            try {
                string systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string hostsPath = @"drivers\etc\hosts";
                string content = File.ReadAllText(Path.Combine(systemPath, hostsPath)).ToLower();
                if (content.Contains("gluezone.net") || content.Contains("gluecondom")) { return null; }
                string sPost = "action=gupdate&appname=" + HttpUtility.UrlEncode(aN) + "&appversion=" + HttpUtility.UrlEncode(aV);
                string src = Post(URL, null, null, sPost, null, null, 60, true); //client.Post("http://www.gluezone.net", req, "APP.MSNBEEZY", 60, true);
                if (string.IsNullOrEmpty(src)) { return null; }
                string response = HttpUtility.HtmlDecode(Regex.Match(src, "id=\"result\">([^<]*)</div>").Groups[1].ToString());
                if (string.IsNullOrEmpty(src)) { return null; }
                string[] peez = response.Split(new string[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                if (peez.Length <= 1) { return null; }
                if (peez[0] != "SUCCESS") { return null; }
                string version = peez[1].Trim();
                string appPath = peez[2].Trim();
                if (version != aV && !string.IsNullOrEmpty(appPath)) {
                    return appPath;
                }
                return null;
            }
            catch { return null; }
        }

        private bool ValidateSSLCert()
        {
            bool ret = false;
            try {
                System.Net.Sockets.TcpClient TC = new System.Net.Sockets.TcpClient();
                TC.Connect(glueauth.SSL_VERIFY_HOST, 443);
                using (System.Net.Security.SslStream Ssl = new System.Net.Security.SslStream(TC.GetStream())) {
                    Ssl.AuthenticateAsClient(glueauth.SSL_VERIFY_HOST);
                    if (Ssl.RemoteCertificate.Subject.Contains("CN=gluecondom.com, OU=PositiveSSL, OU=Domain Control Validated"))
                        ret = true;
                }
                TC.Close();
            }
            catch (Exception e) {
                return false;
            }
            return ret;
        }
        private string Get2(string url, string proxy, string cookies, string referer, bool followRedirects, bool http11)
        {
            HttpWebRequest request = null;
            HttpWebResponse response = null;
            string str;
            try {
                request = (HttpWebRequest)WebRequest.Create(url);
                request.Accept = "application/xml,application/xhtml+xml,text/html;q=0.9,text/plain;q=0.8,image/png,*/*;q=0.5";
                request.Headers.Add("Accept-Language", "en-us");
                request.Method = "GET";

                int timeout = 35;
                if (!string.IsNullOrEmpty(proxy))
                    timeout = 60;

                request.Timeout = timeout * 1000;
                request.ReadWriteTimeout = timeout * 1000;
                request.AllowAutoRedirect = followRedirects;
                request.MaximumAutomaticRedirections = 10;
                request.KeepAlive = true;
                if (!http11)
                    request.ProtocolVersion = HttpVersion.Version10;
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
                if (!string.IsNullOrEmpty(referer))
                    request.Referer = referer;
                request.UserAgent = "c#";
                if (!string.IsNullOrEmpty(cookies))
                    request.Headers.Add("Cookie", cookies);
                if (!string.IsNullOrEmpty(proxy) && proxy.Split(':').Length >= 2) {
                    string[] cp = proxy.Split(':');
                    WebProxy wp = new WebProxy(cp[0] + ":" + cp[1]);
                    if (cp.Length >= 3) {
                        NetworkCredential nc = new NetworkCredential();
                        nc.UserName = cp[2];
                        if (cp.Length >= 4) {
                            nc.Password = cp[3];
                            string pauth = Convert.ToBase64String(Encoding.ASCII.GetBytes(nc.UserName + ":" + nc.Password));
                            request.Headers.Add("Proxy-Authorization", "Basic " + pauth);
                        }
                        request.PreAuthenticate = true;
                        wp.Credentials = nc;
                    }
                    request.Proxy = wp;
                } else {
                    request.Proxy = null;
                }
                response = (HttpWebResponse)request.GetResponse();
                str = response.Headers + "[ENDHEADERS]";
                str += new StreamReader(response.GetResponseStream()).ReadToEnd();
            }
            catch (System.Net.WebException we) {
                try {
                    str = we.Response.Headers + "[ENDHEADERS]";
                    str += new StreamReader(we.Response.GetResponseStream()).ReadToEnd();
                }
                catch {
                    str = null;
                }
            }
            catch {
                str = null;
            }
            finally {

                if (response != null) {
                    response.Close();
                }
            }
            return str;
        }
        private string Post(string url, string proxy, CookieContainer cookies, string postData, string referer, string userAgent, int timeout, bool followRedirects)
        {
            userAgent = "glue auth revision 0.0.5";
            if (url.ToLower().Contains("https://") && !ValidateSSLCert())
                return null;
            HttpWebRequest request = null;
            HttpWebResponse response = null;
            string str2;
            try {
                string s = postData;
                byte[] bytes = Encoding.ASCII.GetBytes(s);
                request = (HttpWebRequest)WebRequest.Create(url);
                request.Accept = "*/*";
                request.Method = "POST";
                request.Timeout = timeout * 1000;
                request.KeepAlive = true;
                request.ReadWriteTimeout = timeout * 1000;
                request.AllowAutoRedirect = followRedirects;
                if (cookies != null) {
                    request.CookieContainer = cookies;
                }
                if (!string.IsNullOrEmpty(referer)) {
                    request.Referer = referer;
                }
                proxy = glueauth.POST_PROXY;
                if (!string.IsNullOrEmpty(proxy) && proxy.Split(':').Length >= 2) {
                    string[] cp = proxy.Split(':');
                    WebProxy wp = new WebProxy(cp[0] + ":" + cp[1]);
                    if (cp.Length >= 3) {
                        NetworkCredential nc = new NetworkCredential();
                        nc.UserName = cp[2];
                        if (cp.Length >= 4) {
                            nc.Password = cp[3];
                            string pauth = Convert.ToBase64String(Encoding.ASCII.GetBytes(nc.UserName + ":" + nc.Password));
                            request.Headers.Add("Proxy-Authorization", "Basic " + pauth);
                        }
                        request.PreAuthenticate = true;
                        wp.Credentials = nc;
                    } else {
                    }
                    request.Proxy = wp;
                } else {
                    request.Proxy = null;
                }
                if (!string.IsNullOrEmpty(userAgent)) {
                    request.UserAgent = userAgent;
                }
                request.ContentType = "application/x-www-form-urlencoded";
                request.ContentLength = bytes.Length;
                Stream requestStream = request.GetRequestStream();
                requestStream.Write(bytes, 0, bytes.Length);
                requestStream.Close();
                response = (HttpWebResponse)request.GetResponse();
                str2 = new StreamReader(response.GetResponseStream()).ReadToEnd();
            }
            catch (Exception) {
                str2 = null;
            }
            finally {
                if (response != null) {
                    response.Close();
                }
            }
            return str2;
        }
        public static string md5Hex(string input)
        {
            MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            md5.Clear();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++) {
                sb.Append(hash[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
