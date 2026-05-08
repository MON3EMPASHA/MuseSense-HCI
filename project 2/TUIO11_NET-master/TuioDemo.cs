/*
	TUIO C# Demo - part of the reacTIVision project
	Copyright (c) 2005-2016 Martin Kaltenbrunner <martin@tuio.org>

	This program is free software; you can redistribute it and/or modify
	it under the terms of the GNU General Public License as published by
	the Free Software Foundation; either version 2 of the License, or
	(at your option) any later version.

	This program is distributed in the hope that it will be useful,
	but WITHOUT ANY WARRANTY; without even the implied warranty of
	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
	GNU General Public License for more details.

	You should have received a copy of the GNU General Public License
	along with this program; if not, write to the Free Software
	Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
*/

using System;
using System.Drawing;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
using TUIO;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Web.Script.Serialization;
using System.Runtime.Serialization.Json;
using System.Media;
using System.Diagnostics;

public class TuioDemo : Form , TuioListener
	{
        int slideIndex = 0;
        string[] slideImages ;
        SolidBrush cardBsh = new SolidBrush(Color.FromArgb(30, 30, 60)); 
        string uname = "Visitor";
        Image upic = null;
        
        // setting default colors and light/dark modes
        private struct ColorTheme
        {
            public Color background;
            public Color cardBackground;
            public Color textDark;
            public Color textLight;
            public Color accentLight; 
            public Color accentBubble;
            public Color avatarBackground;
            public Color border;
        }
        
        private ColorTheme lightTheme = new ColorTheme
        {
            background = Color.FromArgb(246, 248, 252),
            cardBackground = Color.FromArgb(255, 255, 255),
            textDark = Color.FromArgb(24, 31, 42),
            textLight = Color.FromArgb(91, 103, 120),
            accentLight = Color.FromArgb(18, 124, 255),
            accentBubble = Color.FromArgb(218, 235, 255),
            avatarBackground = Color.FromArgb(224, 235, 255),
            border = Color.FromArgb(218, 226, 238)
        };
        
        private ColorTheme darkTheme = new ColorTheme
        {
            background = Color.FromArgb(14, 18, 27),
            cardBackground = Color.FromArgb(25, 31, 44),
            textDark = Color.FromArgb(238, 243, 250),
            textLight = Color.FromArgb(160, 172, 190),
            accentLight = Color.FromArgb(64, 196, 255),
            accentBubble = Color.FromArgb(23, 71, 95),
            avatarBackground = Color.FromArgb(33, 51, 76),
            border = Color.FromArgb(52, 62, 80)
        };

        private ColorTheme currentTheme = new ColorTheme();
        private string currentThemeMode = "light";
        
        private TuioClient client;
		private Dictionary<long,TuioObject> objectList;
		private Dictionary<long,TuioCursor> cursorList;
		private Dictionary<long,TuioBlob> blobList;

		public static int width, height;
		private int window_width =  640;
		private int window_height = 480;
		private int window_left = 0;
		private int window_top = 0;
		private int screen_width = Screen.PrimaryScreen.Bounds.Width;
		private int screen_height = Screen.PrimaryScreen.Bounds.Height;

		private bool fullscreen;
		private bool verbose;

		Font font = new Font("Arial", 10.0f);
		SolidBrush fntBrush = new SolidBrush(Color.FromArgb(40, 40, 40));
		SolidBrush textLightBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
		SolidBrush bgrBrush = new SolidBrush(Color.FromArgb(245, 246, 248));       // changes by gender
		SolidBrush cardBsh_dynamic = new SolidBrush(Color.FromArgb(255, 255, 255));
		SolidBrush accentBrush = new SolidBrush(Color.FromArgb(0, 122, 255));
		SolidBrush avatarBrush = new SolidBrush(Color.FromArgb(220, 230, 250));
		Pen borderPen = new Pen(Color.FromArgb(230, 230, 230), 1);
		SolidBrush curBrush = new SolidBrush(Color.FromArgb(192, 0, 192));
		SolidBrush objBrush = new SolidBrush(Color.FromArgb(64, 0, 0));
		SolidBrush blbBrush = new SolidBrush(Color.FromArgb(200, 220, 255));
        private Panel pnlCard;
        private Label lblHello;
        private PictureBox pictureBox1;
        private Label lblStatus;
        class ArtifactRecord
        {
                public int id { get; set; }
                public int tuioId { get; set; }
                public string name { get; set; }
                public string birthDate { get; set; }
                public string era { get; set; }
                public string origin { get; set; }
                public string description { get; set; }
                public string narration { get; set; }
                public string objPath { get; set; }
                public string audioPath { get; set; }
                public string color { get; set; }
                public string country { get; set; }
        }
        class ArtifactRoot
        {
                public List<ArtifactRecord> artifacts { get; set; }
        }

        class UserRecord
        {
                public string name { get; set; }
                public string age { get; set; }
                public string gender { get; set; }
                public string[] mac { get; set; }
                public string Profile { get; set; }
                public List<int> favorites { get; set; }
                public string themeMode { get; set; }
        }

        class UserRoot
        {
                public List<UserRecord> artifacts { get; set; } // Keep the same property name for JSON compatibility
        }

        class ArtifactClickTarget
        {
                public Rectangle Bounds { get; set; }
                public int ArtifactId { get; set; }
        }

        class PageClickTarget
        {
                public Rectangle Bounds { get; set; }
                public int PageIndex { get; set; }
        }

        List<ArtifactRecord> artifacts = new List<ArtifactRecord>();
        List<ArtifactClickTarget> artifactClickTargets = new List<ArtifactClickTarget>();
        List<PageClickTarget> pageClickTargets = new List<PageClickTarget>();
        int selectedArtifactId = -1;
        string artifactsJsonPath = "";
        Pen curPen = new Pen(new SolidBrush(Color.Blue), 1);
        
        // User data
        UserRecord currentUser = null;
        List<UserRecord> allUsers = new List<UserRecord>();
        string usersJsonPath = "";
        int favoritesPageIndex = 0;
        string artifactFavoriteHint = "Make a CIRCLE to add to favorites!";

        // Circular menu control
        bool tuioMarker100Visible = false;
        int selectedMenuItem = -1; // -1=none, 0=Home, 1=Profile, 2=Artifacts, 3=Favorites, 4=Explore
        long tuioMarker100SessionId = -1;
        TuioClient tuioClient;
        Client socketClient;
        int lastMarkerSent = -1;
        
        SoundPlayer currentAudioPlayer = null;
        int playingArtifactId = -1;
        bool audioMuted = false;
        Rectangle audioToggleButtonRect = Rectangle.Empty;
        Rectangle favoriteToggleButtonRect = Rectangle.Empty;
        Rectangle themeToggleButtonRect = Rectangle.Empty;
        
        PictureBox artifact3DPictureBox;
        
        System.Timers.Timer menuSelectTimer;
        const int UI_REFRESH_MS = 100;
        const int MENU_SELECT_DELAY_MS = 700;

		public TuioDemo(int port) {
        System.Timers.Timer slideTimer = new System.Timers.Timer(3000);
        slideTimer.Elapsed += (s, e) => { slideIndex = (slideIndex + 1) % 5; ; Invoke((Action)Invalidate); };
        slideTimer.Start();
        
        System.Timers.Timer uiTimer = new System.Timers.Timer(UI_REFRESH_MS);
        uiTimer.Elapsed += (s, e) => { try { Invoke((Action)Invalidate); } catch { } };
        uiTimer.Start();
        
        menuSelectTimer = new System.Timers.Timer(MENU_SELECT_DELAY_MS);
        menuSelectTimer.AutoReset = false;
        menuSelectTimer.Elapsed += (s, e) => {
            if (tuioMarker100Visible)
            {
                if (selectedMenuItem >= 0 && selectedMenuItem <= 4)
                {
                    page = selectedMenuItem;
                    selectedArtifactId = -1; // reset artifact selection
                }
                selectedMenuItem = -1;
                tuioMarker100Visible = false;
                tuioMarker100SessionId = -1;
                try { Invoke((Action)Invalidate); } catch { }
            }
        };

        verbose = false;
			fullscreen = false;
			width = window_width;
			height = window_height;


			this.ClientSize = new System.Drawing.Size(width, height);
			this.Name = "TuioDemo";
			this.Text = "TuioDemo";
        this.WindowState = FormWindowState.Maximized;
        this.FormBorderStyle = FormBorderStyle.None;

        artifact3DPictureBox = new PictureBox();
        artifact3DPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        artifact3DPictureBox.Visible = false;
        this.Controls.Add(artifact3DPictureBox);
        ApplyThemeMode("light");

        this.Closing+=new CancelEventHandler(Form_Closing);
			this.KeyDown+=new KeyEventHandler(Form_KeyDown);
            this.MouseDown += new MouseEventHandler(Form_MouseDown);

			this.SetStyle( ControlStyles.AllPaintingInWmPaint |
							ControlStyles.UserPaint |
							ControlStyles.DoubleBuffer, true);

			objectList = new Dictionary<long,TuioObject>(128);
			cursorList = new Dictionary<long,TuioCursor>(128);
			blobList   = new Dictionary<long,TuioBlob>(128);

			tuioClient = new TuioClient(port);
			tuioClient.addTuioListener(this);

			tuioClient.connect();
			Thread socketThread = new Thread(stream);
			socketThread.IsBackground = true;
			socketThread.Start();//this right here is to recive stuff from our python code: hand gestures and facial recognition
            LoadArtifacts();
            LoadUsers();
    }

		private void Form_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e) {

 			if ( e.KeyData == Keys.F1) {
	 			if (fullscreen == false) {

					width = screen_width;
					height = screen_height;

					window_left = this.Left;
					window_top = this.Top;

					this.FormBorderStyle = FormBorderStyle.None;
		 			this.Left = 0;
		 			this.Top = 0;
		 			this.Width = screen_width;
		 			this.Height = screen_height;

		 			fullscreen = true;
	 			} else {

					width = window_width;
					height = window_height;

		 			this.FormBorderStyle = FormBorderStyle.Sizable;
		 			this.Left = window_left;
		 			this.Top = window_top;
		 			this.Width = window_width;
		 			this.Height = window_height;

		 			fullscreen = false;
	 			}
 			} else if ( e.KeyData == Keys.Escape) {
				this.Close();

 			} else if ( e.KeyData == Keys.V ) {
 				verbose=!verbose;
 			} else if ( e.KeyData == Keys.Right ) {
                NavigateNextPage();
 			} else if ( e.KeyData == Keys.Left ) {
                NavigatePreviousPage();
 			}

 		}

        private void Form_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            for (int i = 0; i < pageClickTargets.Count; i++)
            {
                PageClickTarget target = pageClickTargets[i];
                if (target.Bounds.Contains(e.Location))
                {
                    GoToPage(target.PageIndex);
                    return;
                }
            }

            if (audioToggleButtonRect.Contains(e.Location))
            {
                ToggleNarration();
                return;
            }

            if (favoriteToggleButtonRect.Contains(e.Location))
            {
                ToggleFavoriteForSelectedArtifact();
                return;
            }

            if (themeToggleButtonRect.Contains(e.Location))
            {
                ToggleThemeMode();
                return;
            }

            for (int i = 0; i < artifactClickTargets.Count; i++)
            {
                ArtifactClickTarget target = artifactClickTargets[i];
                if (target.Bounds.Contains(e.Location))
                {
                    OpenArtifactDetails(target.ArtifactId);
                    return;
                }
            }
        }

		private void Form_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			client.removeTuioListener(this);

			client.disconnect();
			System.Environment.Exit(0);
		}

		public void addTuioObject(TuioObject o) {
			lock(objectList) {
				objectList.Add(o.SessionID,o);
			} if (verbose) Console.WriteLine("add obj "+o.SymbolID+" ("+o.SessionID+") "+o.X+" "+o.Y+" "+o.Angle);
            
            // Handle circular menu marker (TUIO ID 100)
            if (o.SymbolID == 100)
            {
                if (menuSelectTimer != null) menuSelectTimer.Stop();
                tuioMarker100Visible = true;
                tuioMarker100SessionId = o.SessionID;
                UpdateMenuSelectionFromRotation(o.Angle);
                Invalidate();
            }
            else if (o.SymbolID == 101)
            {
                audioMuted = !audioMuted;
                if (audioMuted)
                {
                    StopAudio();
                    Console.WriteLine("Audio Muted by Marker 101");
                }
                else
                {
                    Console.WriteLine("Audio Unmuted by Marker 101");
                    if (page == 5 && playingArtifactId != -1)
                    {
                        ArtifactRecord artifact = GetArtifactById(playingArtifactId);
                        if (artifact != null) PlayAudio(artifact.audioPath);
                    }
                }
            }
            else if (o.SymbolID == 102)
            {
                ToggleThemeMode();
            }
            else
            {
                NavigateToArtifactByMarker(o.SymbolID);
            }
		}

		public void updateTuioObject(TuioObject o) {

			if (verbose) Console.WriteLine("set obj "+o.SymbolID+" "+o.SessionID+" "+o.X+" "+o.Y+" "+o.Angle+" "+o.MotionSpeed+" "+o.RotationSpeed+" "+o.MotionAccel+" "+o.RotationAccel);
            
            // Handle circular menu marker (TUIO ID 100)
            if (o.SymbolID == 100)
            {
                if (menuSelectTimer != null) menuSelectTimer.Stop();
                tuioMarker100Visible = true;
                tuioMarker100SessionId = o.SessionID;
                UpdateMenuSelectionFromRotation(o.Angle);
                Invalidate();
            }
            else if (o.SymbolID == 101 || o.SymbolID == 102)
            {
                return;
            }
            else
            {
                NavigateToArtifactByMarker(o.SymbolID);
            }
		}

		public void removeTuioObject(TuioObject o) {
			lock(objectList) {
				objectList.Remove(o.SessionID);
			} if (verbose) Console.WriteLine("del obj "+o.SymbolID+" ("+o.SessionID+")");
            
            if (o.SymbolID == 100 && o.SessionID == tuioMarker100SessionId)
            {
                // When marker is lifted, keep the menu visible briefly and then commit the selected item.
                if (menuSelectTimer != null) 
                {
                    menuSelectTimer.Stop();
                    menuSelectTimer.Start();
                }
            }
		}

		public void addTuioCursor(TuioCursor c) {
			lock(cursorList) {
				cursorList.Add(c.SessionID,c);
			}
			if (verbose) Console.WriteLine("add cur "+c.CursorID + " ("+c.SessionID+") "+c.X+" "+c.Y);
		}

		public void updateTuioCursor(TuioCursor c) {
			if (verbose) Console.WriteLine("set cur "+c.CursorID + " ("+c.SessionID+") "+c.X+" "+c.Y+" "+c.MotionSpeed+" "+c.MotionAccel);
		}

		public void removeTuioCursor(TuioCursor c) {
			lock(cursorList) {
				cursorList.Remove(c.SessionID);
			}
			if (verbose) Console.WriteLine("del cur "+c.CursorID + " ("+c.SessionID+")");
 		}

		public void addTuioBlob(TuioBlob b) {
			lock(blobList) {
				blobList.Add(b.SessionID,b);
			}
			if (verbose) Console.WriteLine("add blb "+b.BlobID + " ("+b.SessionID+") "+b.X+" "+b.Y+" "+b.Angle+" "+b.Width+" "+b.Height+" "+b.Area);
		}

		public void updateTuioBlob(TuioBlob b) {
		
			if (verbose) Console.WriteLine("set blb "+b.BlobID + " ("+b.SessionID+") "+b.X+" "+b.Y+" "+b.Angle+" "+b.Width+" "+b.Height+" "+b.Area+" "+b.MotionSpeed+" "+b.RotationSpeed+" "+b.MotionAccel+" "+b.RotationAccel);
		}

		public void removeTuioBlob(TuioBlob b) {
			lock(blobList) {
				blobList.Remove(b.SessionID);
			}
			if (verbose) Console.WriteLine("del blb "+b.BlobID + " ("+b.SessionID+")");
		}

		public void refresh(TuioTime frameTime) {
			Invalidate();
		}
    class Client
    {
        public NetworkStream stream;
        public TcpClient client;
        public StreamReader reader;
        public StreamWriter writer;
        private readonly object writeLock = new object();

        public bool connectToSocket(string host, int portNumber)
        {
            try
            {
                client = new TcpClient(host, portNumber);
                stream = client.GetStream();
                reader = new StreamReader(stream, Encoding.UTF8);
                writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                Console.WriteLine("connection made ! with " + host);
                return true;
            }
            catch (System.Net.Sockets.SocketException e)
            {
                Console.WriteLine("Connection Failed: " + e.Message);
                return false;
            }
        }

        public string recieveMessage()
        {
            try
            {
                if (reader == null) return null;
                string data = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(data)) return null;
                Console.WriteLine(data);
                return data;
            }
            catch (System.Exception)
            {

            }

            return null;
        }

        public void sendMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || writer == null) return;

            lock (writeLock)
            {
                writer.WriteLine(message);
            }
        }
    }
    string msg = "";
	string oldmsg = "";
    int login = 0;
    int page = 0; // 0=Home, 1=Profile, 2=Artifacts, 3=Favorites, 4=Explore, 5=Detail
    string btStatus = "Connecting to Vision Engine...";
    string cameraStatusStr = "Offline";
    DateTime lastGestureTime = DateTime.MinValue;

    // load artifacts text/image data from artifacts.json
    void LoadArtifacts()
    {
        string path = @"..\..\artifacts.json";

        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                ArtifactRoot root = serializer.Deserialize<ArtifactRoot>(json);
                if (root != null && root.artifacts != null && root.artifacts.Count > 0)
                {
                    artifacts = root.artifacts;
                    artifactsJsonPath = Path.GetFullPath(path);
                    Console.WriteLine("Loaded artifacts from: " + artifactsJsonPath + " (count=" + artifacts.Count + ")");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed loading artifacts from " + Path.GetFullPath(path) + ": " + ex.Message);
            }
        }

        Console.WriteLine("No valid artifacts.json could be loaded.");
    }

    // load users data from users.json
    void LoadUsers()
    {
        string path = ResolveUsersJsonPath();

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                List<UserRecord> userList = serializer.Deserialize<List<UserRecord>>(json);
                if (userList != null && userList.Count > 0)
                {
                    foreach (UserRecord user in userList)
                    {
                        user.themeMode = NormalizeThemeMode(user.themeMode);
                    }
                    allUsers = userList;
                    usersJsonPath = Path.GetFullPath(path);
                    Console.WriteLine("Loaded users from: " + usersJsonPath + " (count=" + allUsers.Count + ")");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed loading users from " + path + ": " + ex.Message);
            }
        }

        Console.WriteLine("No valid users.json could be loaded.");
    }

    string ResolveUsersJsonPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates = new string[]
        {
            Path.Combine(baseDir, "users.json"),
            Path.Combine(baseDir, @"..\..\bin\Debug\users.json"),
            Path.Combine(baseDir, @"..\..\users.json"),
            Path.Combine(baseDir, @"..\..\..\bin\Debug\users.json")
        };

        foreach (string candidate in candidates)
        {
            string fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath)) return fullPath;
        }

        return string.Empty;
    }

    // get user by name
    UserRecord GetUserByName(string userName)
    {
        foreach (UserRecord user in allUsers)
        {
            if (user.name == userName) return user;
        }
        return null;
    }

    UserRecord GetUserByMac(string macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress)) return null;

        foreach (UserRecord user in allUsers)
        {
            if (user.mac == null) continue;

            foreach (string mac in user.mac)
            {
                if (string.Equals(mac, macAddress, StringComparison.OrdinalIgnoreCase))
                    return user;
            }
        }

        return null;
    }

    // add artifact to user's favorites
    bool AddArtifactToFavorites(int artifactId)
    {
        if (currentUser == null) return false;
        
        if (currentUser.favorites == null)
            currentUser.favorites = new List<int>();
        
        if (!currentUser.favorites.Contains(artifactId))
        {
            currentUser.favorites.Add(artifactId);
            SaveCurrentUser();
            return true;
        }

        return false;
    }

    // Reload users.json and refresh the logged-in user's record.
    bool RefreshCurrentUserFromUsersFile()
    {
        LoadUsers();

        if (string.IsNullOrWhiteSpace(uname) || uname == "Visitor")
        {
            currentUser = null;
            return false;
        }

        currentUser = GetUserByName(uname);
        return currentUser != null;
    }

    // remove artifact from user's favorites
    void RemoveArtifactFromFavorites(int artifactId)
    {
        if (currentUser == null || currentUser.favorites == null) return;
        
        currentUser.favorites.Remove(artifactId);
        SaveCurrentUser();
    }

    bool IsFavoriteArtifact(int artifactId)
    {
        if (currentUser == null || currentUser.favorites == null) return false;
        return currentUser.favorites.Contains(artifactId);
    }

    // save user preferences back to users.json
    void SaveCurrentUser()
    {
        if (string.IsNullOrWhiteSpace(usersJsonPath) || currentUser == null) return;

        try
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(allUsers);
            File.WriteAllText(usersJsonPath, json);
            Console.WriteLine("Saved user preferences for: " + currentUser.name);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to save user preferences: " + ex.Message);
        }
    }

    // get artifact by marker id
    ArtifactRecord GetArtifactByTuioId(int markerId)
    {
        foreach (ArtifactRecord artifact in artifacts)
        {
            if (artifact.tuioId == markerId) return artifact;
        }
        return null;
    }

    // get artifact by normal id
    ArtifactRecord GetArtifactById(int artifactId)
    {
        foreach (ArtifactRecord artifact in artifacts)
        {
            if (artifact.id == artifactId) return artifact;
        }
        return null;
    }


    private string NormalizeThemeMode(string mode)
    {
        if (!string.IsNullOrWhiteSpace(mode) && mode.Trim().Equals("dark", StringComparison.OrdinalIgnoreCase))
            return "dark";
        return "light";
    }

    private void ApplyThemeMode(string mode)
    {
        currentThemeMode = NormalizeThemeMode(mode);
        currentTheme = currentThemeMode == "dark" ? darkTheme : lightTheme;

        bgrBrush.Color = currentTheme.background;
        cardBsh.Color = currentTheme.cardBackground;
        cardBsh_dynamic.Color = currentTheme.cardBackground;
        fntBrush.Color = currentTheme.textDark;
        textLightBrush.Color = currentTheme.textLight;
        accentBrush.Color = currentTheme.accentLight;
        avatarBrush.Color = currentTheme.avatarBackground;
        blbBrush.Color = currentTheme.accentBubble;
        borderPen.Color = currentTheme.border;

        if (artifact3DPictureBox != null)
        {
            artifact3DPictureBox.BackColor = currentTheme.cardBackground;
        }

        Console.WriteLine("Theme applied: " + currentThemeMode);
    }

    private void ApplyUserTheme(UserRecord user)
    {
        ApplyThemeMode(user != null ? user.themeMode : "light");
    }

    private void ToggleThemeMode()
    {
        string nextMode = currentThemeMode == "dark" ? "light" : "dark";
        ApplyThemeMode(nextMode);

        if (currentUser != null)
        {
            currentUser.themeMode = nextMode;
            SaveCurrentUser();
        }

        Console.WriteLine("TUIO 102 toggled theme to " + nextMode);
        Invalidate();
    }

    private string ResolveAudioPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string absPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\", path));
        if (File.Exists(absPath)) return absPath;
        return null;
    }

    private string Resolve3DModelPath(string artifactName)
    {
        string modelsDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "3d models"));
        if (!Directory.Exists(modelsDir)) return null;
        
        try
        {
            if (artifactName.Contains("Tutankhamun")) 
            {
                var files = Directory.GetFiles(Path.Combine(modelsDir, "Mask of Tutankhamun"), "*.obj", SearchOption.AllDirectories);
                if (files.Length > 0) return files[0];
            }
            else if (artifactName.Contains("Ramses"))
            {
                var files = Directory.GetFiles(Path.Combine(modelsDir, "Ramses II statue at the Grand Egyptian Museum"), "*.obj", SearchOption.AllDirectories);
                if (files.Length > 0) return files[0];
            }
            else if (artifactName.Contains("Senwosret"))
            {
                var files = Directory.GetFiles(Path.Combine(modelsDir, "King Senwosret III (1836-1818 BC)"), "*.obj", SearchOption.AllDirectories);
                if (files.Length > 0) return files[0];
            }
            else if (artifactName.Contains("Nefertiti"))
            {
                var files = Directory.GetFiles(Path.Combine(modelsDir, "bust-of-nefertiti"), "*.obj", SearchOption.AllDirectories);
                if (files.Length > 0) return files[0];
            }
            else if (artifactName.Contains("Horus"))
            {
                var files = Directory.GetFiles(Path.Combine(modelsDir, "Horus"), "*.obj", SearchOption.AllDirectories);
                if (files.Length > 0) return files[0];
            }
            else if (artifactName.Contains("Scarab"))
            {
                string file = Path.Combine(modelsDir, "uploads_files_5401330_beetle.obj");
                if (File.Exists(file)) return file;
            }
            else if (artifactName.Contains("Sphinx"))
            {
                string file = Path.Combine(modelsDir, "uploads_files_4313395_Abolhole-PBR.obj");
                if (File.Exists(file)) return file;
            }
        } catch { }
        return null;
    }

    private string Resolve3DModelGifPath(string artifactName)
    {
        string objPath = Resolve3DModelPath(artifactName);
        if (objPath != null)
        {
            string gifPath = Path.ChangeExtension(objPath, ".gif");
            if (File.Exists(gifPath)) return gifPath;
        }
        return null;
    }

    private void PlayAudio(string path)
    {
        if (audioMuted) return;
        string fullPath = ResolveAudioPath(path);
        if (fullPath != null)
        {
            try {
                if (currentAudioPlayer != null) currentAudioPlayer.Stop();
                currentAudioPlayer = new SoundPlayer(fullPath);
                currentAudioPlayer.Play();
            } catch { }
        }
    }

    private void StopAudio()
    {
        try {
            if (currentAudioPlayer != null) {
                currentAudioPlayer.Stop();
                currentAudioPlayer.Dispose();
                currentAudioPlayer = null;
            }
        } catch { }
    }

    private void ToggleNarration()
    {
        audioMuted = !audioMuted;

        if (audioMuted)
        {
            StopAudio();
        }
        else if (page == 5 && selectedArtifactId >= 0)
        {
            ArtifactRecord artifact = GetArtifactById(selectedArtifactId);
            if (artifact != null) PlayAudio(artifact.audioPath);
        }

        Invalidate();
    }

    private void ToggleFavoriteForSelectedArtifact()
    {
        if (selectedArtifactId < 0 || currentUser == null) return;

        if (IsFavoriteArtifact(selectedArtifactId))
        {
            RemoveArtifactFromFavorites(selectedArtifactId);
            artifactFavoriteHint = "Artifact removed from favourites";
        }
        else
        {
            if (AddArtifactToFavorites(selectedArtifactId))
                artifactFavoriteHint = "Artifact added to favourites";
        }

        Invalidate();
    }

    // find the real image path from objPath field in json
    string ResolveArtifactAssetPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return "";
        if (Path.IsPathRooted(relativePath) && File.Exists(relativePath)) return relativePath;

        string fromJsonFolder = "";
        if (!string.IsNullOrWhiteSpace(artifactsJsonPath))
        {
            string jsonFolder = Path.GetDirectoryName(artifactsJsonPath);
            fromJsonFolder = Path.Combine(jsonFolder, relativePath);
        }

        string[] candidates = new string[]
        {
            relativePath,
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath),
            fromJsonFolder
        };

        foreach (string path in candidates)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
        }

        return relativePath;
    }

    // update menu selection based on TUIO marker rotation
    void UpdateMenuSelectionFromRotation(double angleRadians)
    {
        double angleDegrees = angleRadians * 180.0 / Math.PI;
        angleDegrees = angleDegrees % 360.0;
        if (angleDegrees < 0) angleDegrees += 360.0;
        
        // Define visual angles for the 5 menu items (0 to 360 format)
        // 0=Home(270), 1=Profile(198), 2=Artifacts(342), 3=Favourites(126), 4=Explore(54)
        double[] targetAngles = { 270.0, 198.0, 342.0, 126.0, 54.0 };
        
        int closestItem = 0;
        double minDiff = 360.0;
        
        for (int i = 0; i < 5; i++)
        {
            double diff = Math.Abs(angleDegrees - targetAngles[i]);
            if (diff > 180.0) diff = 360.0 - diff;
            if (diff < minDiff)
            {
                minDiff = diff;
                closestItem = i;
            }
        }
        
        selectedMenuItem = closestItem;
        
        if (verbose)
            Console.WriteLine("Menu selection updated: angle=" + angleDegrees.ToString("F1") + "° -> item=" + selectedMenuItem);
    }

    // when marker appears, jump directly to its artifact page
    void NavigateToArtifactByMarker(int markerId)
    {
        if (artifacts.Count == 0) return;

        ArtifactRecord artifact = GetArtifactByTuioId(markerId);
        if (artifact == null) return;

        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                selectedArtifactId = artifact.id;
                artifactFavoriteHint = "Make a CIRCLE to add to favorites!";
                page = 5;
                SendMarkerUpdate(markerId);
                Invalidate();
            });
            return;
        }

        selectedArtifactId = artifact.id;
        artifactFavoriteHint = "Make a CIRCLE to add to favorites!";
        page = 5;
        SendMarkerUpdate(markerId);
        Invalidate();
    }

    void SendMarkerUpdate(int markerId)
    {
        if (socketClient == null) return;
        if (markerId == lastMarkerSent) return;
        lastMarkerSent = markerId;
        socketClient.sendMessage("TUIO:" + markerId);
    }

    void OpenArtifactDetails(int artifactId)
    {
        ArtifactRecord artifact = GetArtifactById(artifactId);
        if (artifact == null) return;

        selectedArtifactId = artifact.id;
        artifactFavoriteHint = "Make a CIRCLE to add to favorites!";
        page = 5;
        Invalidate();
    }

    void GoToPage(int pageIndex)
    {
        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                if (pageIndex == 3 || pageIndex == 6) { RefreshCurrentUserFromUsersFile(); favoritesPageIndex = 0; }
                page = pageIndex;
                Invalidate();
            });
            return;
        }

        if (pageIndex == 3 || pageIndex == 6) { RefreshCurrentUserFromUsersFile(); favoritesPageIndex = 0; }
        page = pageIndex;
        Invalidate();
    }

    void NavigateNextPage()
    {
        if (page < 4) GoToPage(page + 1);
        else if (page == 4) GoToPage(0);
    }

    void NavigatePreviousPage()
    {
        if (page > 0 && page <= 4) GoToPage(page - 1);
        else if (page == 0) GoToPage(4);
    }


    class LoginPayload
    {
        public string type { get; set; }
        public string name { get; set; }
        public string age { get; set; }
        public string gender { get; set; }
        public string mac { get; set; }
        public string Profile { get; set; }
        public string themeMode { get; set; }
        public string error { get; set; }
    }

    private bool TryHandleLoginPayload(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage) || !rawMessage.TrimStart().StartsWith("{"))
        {
            return false;
        }

        try
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            LoginPayload payload = serializer.Deserialize<LoginPayload>(rawMessage);

            if (payload == null || !string.Equals(payload.type, "user_login", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            uname = string.IsNullOrWhiteSpace(payload.name) ? "Visitor" : payload.name.Trim();

            // Set current user
            currentUser = GetUserByName(uname);
            if (currentUser == null)
                currentUser = GetUserByMac(payload.mac);

            string profilePath = string.IsNullOrWhiteSpace(payload.Profile) ? null : payload.Profile.Trim();
            if (!string.IsNullOrWhiteSpace(profilePath))
            {
                string absoluteProfilePath = Path.IsPathRooted(profilePath)
                    ? profilePath
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, profilePath);

                upic = File.Exists(absoluteProfilePath) ? Image.FromFile(absoluteProfilePath) : null;
            }
            else
            {
                upic = null;
            }

            login = 1;
            
            if (!string.IsNullOrEmpty(payload.error))
            {
                btStatus = payload.error;
            }
            else
            {
                btStatus = "Matched";
            }
            
            // load the user's saved light/dark theme
            if (currentUser != null)
            {
                ApplyUserTheme(currentUser);
            }
            else
            {
                ApplyThemeMode(payload.themeMode);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to parse login payload: " + ex.Message);
            return false;
        }
    }

    public void stream()
    {
		
        Client c = new Client();
        if (!c.connectToSocket("localhost", 5000))    
        {
            Console.WriteLine("Could not connect.");
            btStatus = "Vision Engine Offline";
            Invoke((Action)(Invalidate));
            return;
        }

        socketClient = c;
        
        cameraStatusStr = "Online";
        btStatus = "Waiting for Bluetooth Device...";
        Invoke((Action)(Invalidate));
        
        while (true)
        {
            msg = c.recieveMessage();
            if (msg == null) // Connection dropped
            {
                cameraStatusStr = "Offline";
                btStatus = "Vision Engine Offline";
                Invoke((Action)(Invalidate));
                break;
            }
            if (string.IsNullOrWhiteSpace(msg))
            {
                continue;
            }
            
            lastGestureTime = DateTime.Now;
            //MessageBox.Show(msg);
            if (msg == "q")
            {
                c.stream.Close();
                c.client.Close();
                Console.WriteLine("Connection Terminated !");
                break;
            }
            if(login==0)
            {
                if (TryHandleLoginPayload(msg))
                {
                    Invoke((Action)(Invalidate));
                    continue;
                }

                string loginSuffix = "is logged in";
                int loginSuffixIndex = msg.IndexOf(loginSuffix, StringComparison.OrdinalIgnoreCase);
                if (loginSuffixIndex >= 0)
                {
                    uname = msg.Substring(0, loginSuffixIndex).Trim();
                    upic = null;
                    login = 1;
                    btStatus = "Matched";
                    currentUser = GetUserByName(uname);
                    
                    // load the user's saved light/dark theme
                    if (currentUser != null)
                    {
                        ApplyUserTheme(currentUser);
                    }
                }
                else
                {
                    btStatus = "No match for this device in the system";
                }
                Invoke((Action)(Invalidate));

            }
           
            else
            {
                if (msg.Trim() == "SwipeRight") NavigateNextPage();
                if (msg.Trim() == "SwipeLeft") NavigatePreviousPage();
                if (msg.Trim() == "Circle" && page == 5 && selectedArtifactId >= 0)
                {
                    if (AddArtifactToFavorites(selectedArtifactId))
                    {
                        artifactFavoriteHint = "Artifact added to favourites";
                        Console.WriteLine("Artifact added to favourites");
                    }
                }
                if ((msg.Trim() == "ZoomIn" || msg.Trim() == "ZoomOut") && page == 5 && selectedArtifactId >= 0)
                {
                    ArtifactRecord artifact = GetArtifactById(selectedArtifactId);
                    if (artifact != null)
                    {
                        string objPath = Resolve3DModelPath(artifact.name);
                        if (objPath != null)
                        {
                            try { Process.Start(objPath); } catch { }
                        }
                    }
                }
                Invoke((Action)(Invalidate));
            }

            oldmsg = msg;
        }
    }
   

    int room = 0;
    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // Stop audio if navigating away from Details page
        if (page != 5)
        {
            if (playingArtifactId != -1)
            {
                StopAudio();
                playingArtifactId = -1;
            }
            if (artifact3DPictureBox.Visible)
            {
                artifact3DPictureBox.Visible = false;
            }
        }

        artifactClickTargets.Clear();
        pageClickTargets.Clear();
        audioToggleButtonRect = Rectangle.Empty;
        favoriteToggleButtonRect = Rectangle.Empty;
        themeToggleButtonRect = Rectangle.Empty;

        // Getting the graphics object
        Graphics g = pevent.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.FillRectangle(bgrBrush, new Rectangle(0, 0, this.ClientSize.Width, this.ClientSize.Height));

        // Top Header Line
        g.DrawLine(borderPen, 0, 105, this.ClientSize.Width, 105);
        
        // Draw Application Title
        Font titleFont = new Font("Segoe UI", 24f, FontStyle.Bold);
        g.DrawString("Smart Egyptian Museum", titleFont, fntBrush, 30, 20);
        Font headerNavFont = new Font("Segoe UI", 10f, FontStyle.Bold);

        string[] headerPages = { "Home", "Profile", "Artifacts", "Favourites", "Explore" };
        int headerNavX = 310;
        int headerNavY = 62;
        int headerNavW = 98;
        int headerNavH = 28;
        int headerNavGap = 10;

        for (int i = 0; i < headerPages.Length; i++)
        {
            Rectangle tabRect = new Rectangle(
                headerNavX + i * (headerNavW + headerNavGap),
                headerNavY,
                headerNavW,
                headerNavH
            );
            bool isActivePage = page == i;
            g.FillRectangle(isActivePage ? blbBrush : cardBsh_dynamic, tabRect);
            g.DrawRectangle(isActivePage ? new Pen(accentBrush.Color, 1) : borderPen, tabRect);
            SizeF tabTextSize = g.MeasureString(headerPages[i], headerNavFont);
            g.DrawString(
                headerPages[i],
                headerNavFont,
                isActivePage ? accentBrush : fntBrush,
                tabRect.X + (tabRect.Width - tabTextSize.Width) / 2,
                tabRect.Y + 6
            );
            pageClickTargets.Add(new PageClickTarget { Bounds = tabRect, PageIndex = i });
        }

        // Draw User Status in Center
        if (uname != "Visitor")
        {
            Font userFont = new Font("Segoe UI", 14f, FontStyle.Regular);
            string userText = "Welcome " + uname;
            SizeF userSize = g.MeasureString(userText, userFont);
            g.DrawString(userText, userFont, fntBrush, (this.ClientSize.Width - userSize.Width) / 2, 15);
            
            string btText = "Bluetooth Connected";
            SizeF btSize = g.MeasureString(btText, userFont);
            g.DrawString(btText, new Font("Segoe UI", 12f, FontStyle.Bold), textLightBrush, (this.ClientSize.Width - btSize.Width) / 2, 45);
        }

        // Draw System Status on Right
        int statusX = this.ClientSize.Width - 250;
        Font statusFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        g.FillEllipse(new SolidBrush(cameraStatusStr == "Online" ? Color.Green : Color.Red), statusX, 20, 12, 12);
        g.DrawString("Camera: " + cameraStatusStr, statusFont, fntBrush, statusX + 20, 18);
        
        bool markerReady = (tuioClient != null && tuioClient.isConnected());
        g.FillEllipse(new SolidBrush(markerReady ? Color.Green : Color.Orange), statusX, 40, 12, 12);
        g.DrawString("Marker Engine: " + (markerReady ? "Ready" : "Waiting"), statusFont, fntBrush, statusX + 20, 38);
        
        bool gestureActive = (DateTime.Now - lastGestureTime).TotalSeconds < 2.0;
        g.FillEllipse(new SolidBrush(gestureActive ? Color.Green : Color.Gray), statusX, 60, 12, 12);
        g.DrawString("Gesture: " + (gestureActive ? "Active" : "Waiting"), statusFont, fntBrush, statusX + 20, 58);

        g.FillEllipse(new SolidBrush(audioMuted ? Color.Red : Color.Green), statusX, 80, 12, 12);
        g.DrawString("Audio: " + (audioMuted ? "Muted 🔇" : "Playing 🔊"), statusFont, fntBrush, statusX + 20, 78);

        string themeLabel = currentThemeMode == "dark" ? "Dark" : "Light";
        SizeF themeSize = g.MeasureString(themeLabel + " mode", statusFont);
        int themeX = statusX - 145;
        Rectangle themeRect = new Rectangle(themeX, 20, (int)themeSize.Width + 30, 30);
        themeToggleButtonRect = themeRect;
        g.FillRectangle(blbBrush, themeRect);
        g.DrawRectangle(borderPen, themeRect);
        g.DrawString(themeLabel + " mode", statusFont, accentBrush, themeX + 15, 27);

        // Draw Page Content
        int contentY = 120;

        if (uname == "Visitor" && page != 5)
        {
            // Login screen
            int cw = 500, ch = 500;
            int cX = (this.ClientSize.Width - cw) / 2;
            int cY = (this.ClientSize.Height - ch) / 2;
           
            g.FillRectangle(cardBsh_dynamic, cX, cY, cw, ch);
            g.DrawRectangle(borderPen, cX, cY, cw, ch);
            g.FillEllipse(avatarBrush, cX + 190, cY + 60, 120, 120);
            Font hellofont = new Font("Segoe UI", 22f, FontStyle.Bold);
            if (upic != null) g.DrawImage(upic, cX + 190, cY + 60, 120, 120);
            else g.FillEllipse(avatarBrush, cX + 190, cY + 60, 120, 120);
            
            g.DrawString("Hello, " + uname, hellofont, fntBrush, cX + 150, cY + 210);
            g.DrawString("Bluetooth Verification", new Font("Segoe UI", 13f), textLightBrush, cX + 140, cY + 270);
            g.DrawString(btStatus, new Font("Segoe UI", 11f, FontStyle.Italic), accentBrush, cX + 120, cY + 330);
        }
        else if (page == 0) // Home
        {
            g.DrawString("Home Page", new Font("Segoe UI", 28f, FontStyle.Bold), fntBrush, 50, contentY);
            g.DrawString("Swipe Left/Right to explore the museum.", new Font("Segoe UI", 14f), textLightBrush, 50, contentY + 60);

            if (artifacts != null && artifacts.Count > 0)
            {
                g.DrawString("Featured Artifacts", new Font("Segoe UI", 18f, FontStyle.Bold), fntBrush, 50, contentY + 120);
                
                int cardW = 280;
                int cardH = 340;
                int gap = 20;
                int startX = 50;
                int startY = contentY + 160;

                for (int i = 0; i < artifacts.Count && i < 3; i++)
                {
                    ArtifactRecord artifact = artifacts[i];
                    int x = startX + i * (cardW + gap);
                    int y = startY;
                    Rectangle cardRect = new Rectangle(x, y, cardW, cardH);

                    g.FillRectangle(cardBsh_dynamic, cardRect);
                    g.DrawRectangle(borderPen, cardRect);
                    artifactClickTargets.Add(new ArtifactClickTarget { Bounds = cardRect, ArtifactId = artifact.id });

                    string imagePath = ResolveArtifactAssetPath(artifact.objPath);
                    if (File.Exists(imagePath))
                    {
                        try
                        {
                            Image artifactImg = Image.FromFile(imagePath);
                            g.DrawImage(artifactImg, x, y, cardW, cardH - 120);
                            artifactImg.Dispose();
                        }
                        catch { }
                    }

                    g.DrawString(artifact.name, new Font("Segoe UI", 12f, FontStyle.Bold), fntBrush, x + 10, y + cardH - 110);
                    g.DrawString(artifact.era, new Font("Segoe UI", 10f), textLightBrush, x + 10, y + cardH - 85);
                }
            }
        }
        else if (page == 1) // Profile
        {
            g.DrawString("User Profile", new Font("Segoe UI", 28f, FontStyle.Bold), fntBrush, 50, contentY);
            int cardX = 50;
            int cardY = contentY + 60;
            int cardW = 920;
            int cardH = 430;

            g.FillRectangle(cardBsh_dynamic, cardX, cardY, cardW, cardH);
            g.DrawRectangle(borderPen, cardX, cardY, cardW, cardH);

            int avatarX = cardX + 35;
            int avatarY = cardY + 35;
            int avatarSize = 220;
            g.FillEllipse(avatarBrush, avatarX, avatarY, avatarSize, avatarSize);

            if (upic != null)
            {
                g.DrawImage(upic, avatarX, avatarY, avatarSize, avatarSize);
            }

            Font labelFont = new Font("Segoe UI", 12f, FontStyle.Bold);
            Font valueFont = new Font("Segoe UI", 15f, FontStyle.Regular);
            Font sectionFont = new Font("Segoe UI", 18f, FontStyle.Bold);
            int infoX = avatarX + avatarSize + 50;
            int lineY = cardY + 45;

            g.DrawString(uname, new Font("Segoe UI", 24f, FontStyle.Bold), fntBrush, infoX, lineY);
            lineY += 55;
            g.DrawString("Visitor Snapshot", sectionFont, accentBrush, infoX, lineY);
            lineY += 45;

            if (currentUser != null)
            {
                g.DrawString("Age", labelFont, textLightBrush, infoX, lineY);
                g.DrawString(currentUser.age, valueFont, fntBrush, infoX + 150, lineY - 2);
                lineY += 42;

                g.DrawString("Gender", labelFont, textLightBrush, infoX, lineY);
                g.DrawString(currentUser.gender, valueFont, fntBrush, infoX + 150, lineY - 2);
                lineY += 42;

                string favoriteCount = currentUser.favorites != null ? currentUser.favorites.Count.ToString() : "0";
                g.DrawString("Favourites", labelFont, textLightBrush, infoX, lineY);
                g.DrawString(favoriteCount, valueFont, fntBrush, infoX + 150, lineY - 2);
                lineY += 42;
            }
            else
            {
                g.DrawString("No detailed user record loaded yet.", valueFont, textLightBrush, infoX, lineY);
                lineY += 42;
            }

            Rectangle statBox1 = new Rectangle(infoX, cardY + 250, 180, 95);
            Rectangle statBox2 = new Rectangle(infoX + 205, cardY + 250, 180, 95);
            Rectangle statBox3 = new Rectangle(infoX + 410, cardY + 250, 180, 95);

            g.FillRectangle(blbBrush, statBox1);
            g.FillRectangle(blbBrush, statBox2);
            g.FillRectangle(blbBrush, statBox3);
            g.DrawRectangle(borderPen, statBox1);
            g.DrawRectangle(borderPen, statBox2);
            g.DrawRectangle(borderPen, statBox3);

            g.DrawString("Profile", labelFont, textLightBrush, statBox1.X + 18, statBox1.Y + 18);
            g.DrawString("Bluetooth matched", new Font("Segoe UI", 13f, FontStyle.Bold), fntBrush, statBox1.X + 18, statBox1.Y + 48);
            g.DrawString("Theme", labelFont, textLightBrush, statBox2.X + 18, statBox2.Y + 18);
            g.DrawString(currentThemeMode == "dark" ? "Dark mode" : "Light mode", new Font("Segoe UI", 13f, FontStyle.Bold), fntBrush, statBox2.X + 18, statBox2.Y + 48);
            g.DrawString("Marker 102", labelFont, textLightBrush, statBox3.X + 18, statBox3.Y + 18);
            g.DrawString("Toggle theme", new Font("Segoe UI", 13f, FontStyle.Bold), fntBrush, statBox3.X + 18, statBox3.Y + 48);
        }
        else if (page == 2) // Artifacts Grid
        {
            g.DrawString("All Artifacts", new Font("Segoe UI", 24f, FontStyle.Bold), fntBrush, 40, contentY);
            
            // Draw mock search/filter bar
            g.FillRectangle(cardBsh_dynamic, 40, contentY + 40, 400, 40);
            g.DrawRectangle(borderPen, 40, contentY + 40, 400, 40);
            g.DrawString("Search by name, era...", new Font("Segoe UI", 10f), textLightBrush, 50, contentY + 50);

            if (artifacts.Count > 0)
            {
                int cardW = 280;
                int cardH = 340;
                int gap = 20;
                int colsPerRow = 3;
                int startX = 40;
                int startY = contentY + 100;

                for (int i = 0; i < artifacts.Count && i < 6; i++)
                {
                    ArtifactRecord artifact = artifacts[i];
                    int col = i % colsPerRow;
                    int row = i / colsPerRow;
                    int x = startX + col * (cardW + gap);
                    int y = startY + row * (cardH + gap);
                    Rectangle cardRect = new Rectangle(x, y, cardW, cardH);

                    g.FillRectangle(cardBsh_dynamic, cardRect);
                    g.DrawRectangle(borderPen, cardRect);
                    artifactClickTargets.Add(new ArtifactClickTarget { Bounds = cardRect, ArtifactId = artifact.id });

                    string imagePath = ResolveArtifactAssetPath(artifact.objPath);
                    if (File.Exists(imagePath))
                    {
                        try
                        {
                            Image artifactImg = Image.FromFile(imagePath);
                            g.DrawImage(artifactImg, x, y, cardW, cardH - 120);
                            artifactImg.Dispose();
                        }
                        catch { }
                    }

                    g.DrawString(artifact.name, new Font("Segoe UI", 12f, FontStyle.Bold), fntBrush, x + 10, y + cardH - 110);
                    g.DrawString(artifact.era, new Font("Segoe UI", 10f), textLightBrush, x + 10, y + cardH - 85);
                    g.DrawString("TUIO: " + artifact.tuioId, new Font("Segoe UI", 9f), accentBrush, x + 10, y + cardH - 65);
                    
                    // mock buttons
                    g.DrawRectangle(borderPen, x + 10, y + cardH - 40, 80, 30);
                    g.DrawString("View", new Font("Segoe UI", 9f), fntBrush, x + 30, y + cardH - 33);
                }
            }
        }
        else if (page == 3 || page == 6) // Favorites List
        {
            RefreshCurrentUserFromUsersFile();
            g.DrawString("My Favourites", new Font("Segoe UI", 24f, FontStyle.Bold), fntBrush, 40, contentY);

            if (currentUser == null || currentUser.favorites == null || currentUser.favorites.Count == 0)
            {
                g.DrawString("No favorites yet", new Font("Segoe UI", 16f), textLightBrush, 50, contentY + 60);
            }
            else
            {
                List<ArtifactRecord> favoriteArtifacts = new List<ArtifactRecord>();
                foreach (int id in currentUser.favorites)
                {
                    ArtifactRecord artifact = GetArtifactById(id);
                    if (artifact != null) favoriteArtifacts.Add(artifact);
                }

                int startX = 40;
                int startY = contentY + 60;
                int itemH = 100;
                int itemW = 800;

                for (int i = 0; i < favoriteArtifacts.Count; i++)
                {
                    ArtifactRecord artifact = favoriteArtifacts[i];
                    int y = startY + i * (itemH + 10);
                    Rectangle itemRect = new Rectangle(startX, y, itemW, itemH);

                    g.FillRectangle(cardBsh_dynamic, itemRect);
                    g.DrawRectangle(borderPen, itemRect);
                    artifactClickTargets.Add(new ArtifactClickTarget { Bounds = itemRect, ArtifactId = artifact.id });

                    string imagePath = ResolveArtifactAssetPath(artifact.objPath);
                    if (File.Exists(imagePath))
                    {
                        try
                        {
                            Image artifactImage = Image.FromFile(imagePath);
                            g.DrawImage(artifactImage, startX + 10, y + 10, 80, 80);
                            artifactImage.Dispose();
                        }
                        catch { }
                    }

                    g.DrawString(artifact.name, new Font("Segoe UI", 14f, FontStyle.Bold), fntBrush, startX + 110, y + 20);
                    g.DrawString(artifact.era, new Font("Segoe UI", 11f), textLightBrush, startX + 110, y + 50);
                    g.DrawString("TUIO: " + artifact.tuioId, new Font("Segoe UI", 10f, FontStyle.Bold), accentBrush, startX + 650, y + 38);
                }
            }
        }
        else if (page == 4) // Explore
        {
            g.DrawString("Explore the Museum Map", new Font("Segoe UI", 28f, FontStyle.Bold), fntBrush, 50, contentY);
            g.DrawString("Suggested zones and quick picks for the current visit.", new Font("Segoe UI", 14f), textLightBrush, 50, contentY + 60);

            Rectangle overviewRect = new Rectangle(50, contentY + 110, 520, 420);
            Rectangle picksRect = new Rectangle(600, contentY + 110, 380, 420);
            g.FillRectangle(cardBsh_dynamic, overviewRect);
            g.FillRectangle(cardBsh_dynamic, picksRect);
            g.DrawRectangle(borderPen, overviewRect);
            g.DrawRectangle(borderPen, picksRect);

            g.DrawString("Museum Route", new Font("Segoe UI", 18f, FontStyle.Bold), fntBrush, overviewRect.X + 20, overviewRect.Y + 20);
            g.DrawString("Ancient Egypt", new Font("Segoe UI", 14f, FontStyle.Bold), accentBrush, overviewRect.X + 35, overviewRect.Y + 80);
            g.DrawString("Royal Collection", new Font("Segoe UI", 14f, FontStyle.Bold), accentBrush, overviewRect.X + 185, overviewRect.Y + 180);
            g.DrawString("Sculpture Hall", new Font("Segoe UI", 14f, FontStyle.Bold), accentBrush, overviewRect.X + 330, overviewRect.Y + 300);

            Pen routePen = new Pen(accentBrush.Color, 4);
            g.DrawEllipse(routePen, overviewRect.X + 40, overviewRect.Y + 120, 18, 18);
            g.DrawEllipse(routePen, overviewRect.X + 200, overviewRect.Y + 220, 18, 18);
            g.DrawEllipse(routePen, overviewRect.X + 355, overviewRect.Y + 340, 18, 18);
            g.DrawLine(routePen, overviewRect.X + 58, overviewRect.Y + 129, overviewRect.X + 200, overviewRect.Y + 229);
            g.DrawLine(routePen, overviewRect.X + 218, overviewRect.Y + 229, overviewRect.X + 355, overviewRect.Y + 349);

            g.DrawString("Use marker 100 to open the circular menu and jump between pages.", new Font("Segoe UI", 12f), textLightBrush, overviewRect.X + 20, overviewRect.Bottom - 75);
            g.DrawString("Place any artifact marker to open its details instantly.", new Font("Segoe UI", 12f), textLightBrush, overviewRect.X + 20, overviewRect.Bottom - 45);

            g.DrawString("Quick Picks", new Font("Segoe UI", 18f, FontStyle.Bold), fntBrush, picksRect.X + 20, picksRect.Y + 20);

            for (int i = 0; i < artifacts.Count && i < 3; i++)
            {
                ArtifactRecord artifact = artifacts[i];
                int cardX = picksRect.X + 20;
                int cardY = picksRect.Y + 65 + i * 110;
                Rectangle artifactRect = new Rectangle(cardX, cardY, picksRect.Width - 40, 90);
                g.FillRectangle(blbBrush, artifactRect);
                g.DrawRectangle(borderPen, artifactRect);
                artifactClickTargets.Add(new ArtifactClickTarget { Bounds = artifactRect, ArtifactId = artifact.id });

                g.DrawString(artifact.name, new Font("Segoe UI", 13f, FontStyle.Bold), fntBrush, artifactRect.X + 16, artifactRect.Y + 14);
                g.DrawString(artifact.era, new Font("Segoe UI", 10f), textLightBrush, artifactRect.X + 16, artifactRect.Y + 42);
                g.DrawString("Marker " + artifact.tuioId, new Font("Segoe UI", 10f, FontStyle.Bold), accentBrush, artifactRect.Right - 105, artifactRect.Y + 32);
            }

            g.DrawString("Swipe to continue exploring, or place a marker to focus on a single artifact.", new Font("Segoe UI", 12f), textLightBrush, picksRect.X + 20, picksRect.Bottom - 40);
        }
        else if (page == 5 && selectedArtifactId >= 0) // Details
        {
            ArtifactRecord artifact = GetArtifactById(selectedArtifactId);
            if (artifact != null)
            {
                if (playingArtifactId != selectedArtifactId)
                {
                    StopAudio();
                    playingArtifactId = selectedArtifactId;
                    PlayAudio(artifact.audioPath);
                }

                int leftW = 600;
                int rightW = 400;
                int startX = 40;
                
                // Left 3D Viewer Panel
                g.FillRectangle(cardBsh_dynamic, startX, contentY, leftW, 500);
                g.DrawRectangle(borderPen, startX, contentY, leftW, 500);
                
                string gifPath = Resolve3DModelGifPath(artifact.name);
                if (gifPath != null)
                {
                    if (artifact3DPictureBox.ImageLocation != gifPath)
                    {
                        artifact3DPictureBox.ImageLocation = gifPath;
                        artifact3DPictureBox.LoadAsync();
                    }
                    artifact3DPictureBox.BackColor = currentTheme.cardBackground;
                    artifact3DPictureBox.Bounds = new Rectangle(startX + 20, contentY + 20, leftW - 40, 460);
                    if (!artifact3DPictureBox.Visible) artifact3DPictureBox.Visible = true;
                }
                else
                {
                    if (artifact3DPictureBox.Visible) artifact3DPictureBox.Visible = false;
                    string imagePath = ResolveArtifactAssetPath(artifact.objPath);
                    if (File.Exists(imagePath))
                    {
                        Image artifactImage = Image.FromFile(imagePath);
                        g.DrawImage(artifactImage, startX + 20, contentY + 20, leftW - 40, 460);
                        artifactImage.Dispose();
                    }
                }
                
                // Right Metadata Panel
                int rightX = startX + leftW + 30;
                g.DrawString("Artifact Metadata", new Font("Segoe UI", 16f, FontStyle.Bold), fntBrush, rightX, contentY);
                
                Font keyFont = new Font("Segoe UI", 12f, FontStyle.Bold);
                Font valFont = new Font("Segoe UI", 12f);
                int lineY = contentY + 40;

                g.DrawString("Name:", keyFont, fntBrush, rightX, lineY);
                g.DrawString(artifact.name, valFont, fntBrush, rightX + 120, lineY);
                lineY += 30;

                g.DrawString("Era:", keyFont, fntBrush, rightX, lineY);
                g.DrawString(artifact.era, valFont, fntBrush, rightX + 120, lineY);
                lineY += 30;

                g.DrawString("Origin:", keyFont, fntBrush, rightX, lineY);
                g.DrawString(artifact.origin, valFont, fntBrush, rightX + 120, lineY);
                lineY += 40;
                
                bool hasAudio = ResolveAudioPath(artifact.audioPath) != null;
                bool has3D = Resolve3DModelPath(artifact.name) != null;
                
                g.DrawString("3D Model:", keyFont, fntBrush, rightX, lineY);
                g.DrawString(has3D ? "Available (Zoom to View)" : "Coming soon", valFont, has3D ? accentBrush : textLightBrush, rightX + 120, lineY);
                lineY += 30;
                
                g.DrawString("Audio:", keyFont, fntBrush, rightX, lineY);
                g.DrawString(hasAudio ? "Playing now" : "Coming soon", valFont, hasAudio ? accentBrush : textLightBrush, rightX + 120, lineY);
                lineY += 40;

                audioToggleButtonRect = new Rectangle(rightX, lineY, 190, 34);
                g.FillRectangle(blbBrush, audioToggleButtonRect);
                g.DrawRectangle(borderPen, audioToggleButtonRect);
                g.DrawString(audioMuted ? "Unmute narration" : "Mute narration", new Font("Segoe UI", 11f, FontStyle.Bold), accentBrush, audioToggleButtonRect.X + 18, audioToggleButtonRect.Y + 8);
                lineY += 54;

                bool isFavorite = IsFavoriteArtifact(artifact.id);
                favoriteToggleButtonRect = new Rectangle(rightX + 210, lineY - 54, 190, 34);
                g.FillRectangle(blbBrush, favoriteToggleButtonRect);
                g.DrawRectangle(borderPen, favoriteToggleButtonRect);
                g.DrawString(isFavorite ? "Remove favourite" : "Add favourite", new Font("Segoe UI", 11f, FontStyle.Bold), accentBrush, favoriteToggleButtonRect.X + 20, favoriteToggleButtonRect.Y + 8);

                g.DrawString("Description:", keyFont, fntBrush, rightX, lineY);
                RectangleF descRect = new RectangleF(rightX, lineY + 26, rightW, 200);
                g.DrawString(artifact.description, valFont, textLightBrush, descRect);

                g.DrawString(artifactFavoriteHint, new Font("Segoe UI", 12f, FontStyle.Bold), accentBrush, rightX, contentY + 450);
            }
        }
        


        // Draw Navigation hint
        g.DrawString("Swipe Left/Right to Navigate  |  Make a CIRCLE to select", new Font("Segoe UI", 11f, FontStyle.Italic), textLightBrush, 40, this.ClientSize.Height - 40);
        
        // Removed TUIO debug drawing for objects, cursors, and blobs to keep UI clean.

        // Draw the circular menu
        DrawCircularMenu(g, this.ClientSize.Width, this.ClientSize.Height);
    }

    private void InitializeComponent()
    {
            this.pnlCard = new System.Windows.Forms.Panel();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.lblHello = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.pnlCard.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // pnlCard
            // 
            this.pnlCard.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(60)))));
            this.pnlCard.Controls.Add(this.lblStatus);
            this.pnlCard.Controls.Add(this.lblHello);
            this.pnlCard.Controls.Add(this.pictureBox1);
            this.pnlCard.Location = new System.Drawing.Point(257, 105);
            this.pnlCard.Name = "pnlCard";
            this.pnlCard.Size = new System.Drawing.Size(500, 500);
            this.pnlCard.TabIndex = 0;
            this.pnlCard.Paint += new System.Windows.Forms.PaintEventHandler(this.panel1_Paint);
            // 
            // pictureBox1
            // 
            this.pictureBox1.Location = new System.Drawing.Point(190, 101);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(120, 120);
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            // 
            // lblHello
            // 
            this.lblHello.AutoSize = true;
            this.lblHello.Font = new System.Drawing.Font("Arial", 22F);
            this.lblHello.ForeColor = System.Drawing.Color.Cornsilk;
            this.lblHello.Location = new System.Drawing.Point(149, 254);
            this.lblHello.Name = "lblHello";
            this.lblHello.Size = new System.Drawing.Size(219, 42);
            this.lblHello.TabIndex = 1;
            this.lblHello.Text = "Hello, Visitor";
            this.lblHello.Click += new System.EventHandler(this.lblHello_Click);
            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.Font = new System.Drawing.Font("Arial", 18F);
            this.lblStatus.ForeColor = System.Drawing.Color.Cornsilk;
            this.lblStatus.Location = new System.Drawing.Point(184, 319);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(140, 35);
            this.lblStatus.TabIndex = 2;
            this.lblStatus.Text = "Waiting...";
            this.lblStatus.Click += new System.EventHandler(this.label1_Click);
            // 
            // TuioDemo
            // 
            this.ClientSize = new System.Drawing.Size(1564, 743);
            this.Controls.Add(this.pnlCard);
            this.Name = "TuioDemo";
            this.Load += new System.EventHandler(this.TuioDemo_Load);
            this.pnlCard.ResumeLayout(false);
            this.pnlCard.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);

    }
	 
    // Draw circular menu with 5 items
    private void DrawCircularMenu(Graphics g, int screenWidth, int screenHeight)
    {
        if (!tuioMarker100Visible) return;

        // Menu configuration
        int radius = 90; 
        int itemSize = 65;
        int centerSize = 90;
        int centerX = screenWidth - radius - itemSize - 30; // Bottom Right
        int centerY = screenHeight - radius - itemSize - 30; 

        string[] menuLabels = { "Home", "Profile", "Artifacts", "Favourites", "Explore" };
        string[] menuIcons = { "🏠", "👤", "🏛", "❤", "🧭" };
        
        // Pentagon layout matching the Update logic:
        // 0=Home (-90), 1=Profile (-162), 2=Artifacts (-18), 3=Favourites (126), 4=Explore (54)
        double[] finalAngles = { -90, -162, -18, 126, 54 };

        // Draw center circle
        SolidBrush centerBrush = new SolidBrush(Color.FromArgb(28, 44, 70)); // Dark blue
        g.FillEllipse(centerBrush, centerX - centerSize/2, centerY - centerSize/2, centerSize, centerSize);
        
        // Try drawing pharaoh mask in center
        try {
            string maskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts", "Tutankhamun.png");
            if (File.Exists(maskPath)) {
                Image maskImg = Image.FromFile(maskPath);
                g.DrawImage(maskImg, centerX - 25, centerY - 30, 50, 60);
                maskImg.Dispose();
            }
        } catch { }

        Font iconFont = new Font("Segoe UI Emoji", 14f);
        Font labelFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        StringFormat format = new StringFormat();
        format.Alignment = StringAlignment.Center;
        
        for (int i = 0; i < 5; i++)
        {
            double radians = finalAngles[i] * Math.PI / 180.0;
            int itemX = centerX + (int)(radius * Math.Cos(radians)) - itemSize / 2;
            int itemY = centerY + (int)(radius * Math.Sin(radians)) - itemSize / 2;

            bool isSelected = (i == selectedMenuItem);
            
            Color bgColor = isSelected ? Color.FromArgb(232, 240, 254) : Color.White;
            SolidBrush itemBrush = new SolidBrush(bgColor);
            g.FillEllipse(itemBrush, itemX, itemY, itemSize, itemSize);

            // Draw border 
            Pen highlightPen = isSelected ? new Pen(Color.FromArgb(66, 133, 244), 3) : new Pen(Color.FromArgb(180, 190, 210), 2);
            g.DrawEllipse(highlightPen, itemX, itemY, itemSize, itemSize);

            SolidBrush textBrush = new SolidBrush(Color.FromArgb(28, 44, 70)); // Dark blue text
            
            // Draw Icon
            g.DrawString(menuIcons[i], iconFont, textBrush, itemX + itemSize / 2, itemY + 10, format);
            
            // Draw label
            g.DrawString(menuLabels[i], labelFont, textBrush, itemX + itemSize / 2, itemY + itemSize / 2 + 10, format);
        }
    }

    private void TuioDemo_Load(object sender, EventArgs e)
    {
        pnlCard.Left = (this.ClientSize.Width - pnlCard.Width) / 2;
        pnlCard.Top = (this.ClientSize.Height - pnlCard.Height) / 2;
    }

    private void panel1_Paint(object sender, PaintEventArgs e)
    {

    }

    private void lblHello_Click(object sender, EventArgs e)
    {

    }

    private void label1_Click(object sender, EventArgs e)
    {

    }

    public static void Main(String[] argv) {
	 		int port = 0;
			switch (argv.Length) {
				case 1:
					port = int.Parse(argv[0],null);
					if(port==0) goto default;
					break;
				case 0:
					port = 3333;
					break;
				default:
					Console.WriteLine("usage: mono TuioDemo [port]");
					System.Environment.Exit(0);
					break;
			}
			
			TuioDemo app = new TuioDemo(port);
			Application.Run(app);
		}
	}
