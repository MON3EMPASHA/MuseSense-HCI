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
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
using TUIO;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Linq;
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

        // Pink theme — applied by default for users whose gender is "female"
        // (and they have not explicitly chosen another theme).
        private ColorTheme pinkTheme = new ColorTheme
        {
            background      = Color.FromArgb(255, 242, 247), // soft blush
            cardBackground  = Color.FromArgb(255, 255, 255),
            textDark        = Color.FromArgb( 70,  20,  45), // deep plum
            textLight       = Color.FromArgb(160,  90, 120),
            accentLight     = Color.FromArgb(226,  70, 140), // hot pink
            accentBubble    = Color.FromArgb(255, 220, 232),
            avatarBackground= Color.FromArgb(255, 214, 230),
            border          = Color.FromArgb(245, 200, 218)
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
            public string role { get; set; }
            public string password { get; set; }
        }

        class UserRoot
        {
                public List<UserRecord> users { get; set; }
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

        // === Adaptive interface (age-based) ===
        enum UIMode { Child, Teen, Adult, Senior }

        class AgeProfile
        {
                public UIMode Mode { get; set; }
                public string Label { get; set; }
                public bool CameraVisible { get; set; }   // controls Python OpenCV "Output" window
                public bool ShowTranscription { get; set; }
                public float FontScale { get; set; }
                public bool ForceHighContrast { get; set; }
                public bool LargeIcons { get; set; }
        }

        static AgeProfile ResolveAgeProfile(int age)
        {
                // Transcription panel intentionally disabled for all ages — the
                // event log (gestures / expressions / TUIO markers) still gets
                // collected internally, just not rendered.
                if (age <= 12) return new AgeProfile {
                        Mode = UIMode.Child,  Label = "Child (basic)",
                        CameraVisible = false, ShowTranscription = false,
                        FontScale = 1.15f, ForceHighContrast = false, LargeIcons = true
                };
                if (age <= 19) return new AgeProfile {
                        Mode = UIMode.Teen,   Label = "Teen (detailed)",
                        CameraVisible = true,  ShowTranscription = false,
                        FontScale = 1.0f,  ForceHighContrast = false, LargeIcons = false
                };
                if (age <= 49) return new AgeProfile {
                        Mode = UIMode.Adult,  Label = "Adult (expert)",
                        CameraVisible = true,  ShowTranscription = false,
                        FontScale = 1.0f,  ForceHighContrast = false, LargeIcons = false
                };
                return new AgeProfile {
                        Mode = UIMode.Senior, Label = "Senior (accessible)",
                        CameraVisible = false, ShowTranscription = false,
                        FontScale = 1.55f, ForceHighContrast = true,  LargeIcons = true
                };
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
        string artifactFavoriteHint = "Make a CIRCLE to toggle favorites!";

        // Adaptive UI profile (set on login from payload.age)
        AgeProfile activeProfile = ResolveAgeProfile(25); // default to adult until login
        readonly List<string> transcriptionLog = new List<string>();
        readonly object transcriptionLock = new object();
        const int MAX_TRANSCRIPTION_LINES = 12;

        // Emotion-reactive effects engine (balloons / ring / drops / toast).
        readonly EmotionEffectEngine emotionEngine = new EmotionEffectEngine();
        System.Timers.Timer effectsTimer;

        // Gaze spotlight (soft radial glow that follows the user's gaze across
        // a 3×3 grid on screen).
        string lastGazeZone = "";          // raw zone from TRANS messages
        float gazeSpotlightX = -1f;        // current animated centre X
        float gazeSpotlightTargetX = -1f;  // where it should be
        float gazeSpotlightY = -1f;        // current animated centre Y
        float gazeSpotlightTargetY = -1f;  // where it should be
        float gazeSpotlightAlpha = 0f;     // 0..1, fades in once we have a gaze fix

        // Circular menu control
        bool tuioMarker100Visible = false;
        int selectedMenuItem = -1; // -1=none, 0=Home, 1=Profile, 2=Artifacts, 3=Favorites, 4=Explore
        long tuioMarker100SessionId = -1;
        TuioClient tuioClient;
        Client socketClient;
        int lastMarkerSent = -1;
        const int TUIO_FAVORITE_TOGGLE_ID = 103;
        
        SoundPlayer currentAudioPlayer = null;
        int playingArtifactId = -1;
        bool audioMuted = false;
        Rectangle audioToggleButtonRect = Rectangle.Empty;
        Rectangle favoriteToggleButtonRect = Rectangle.Empty;
        Rectangle themeToggleButtonRect = Rectangle.Empty;

        const int ADMIN_AUTH_MARKER_ID = 110;
        
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

        // 30 fps repaint while emotion effects are alive (balloons need smoothness).
        // Stays idle most of the time and starts/stops itself based on engine state.
        effectsTimer = new System.Timers.Timer(33);
        effectsTimer.AutoReset = true;
        effectsTimer.Elapsed += (s, e) =>
        {
            if (emotionEngine.HasActiveEffects)
            {
                try { Invoke((Action)Invalidate); } catch { }
            }
            else
            {
                try { effectsTimer.Stop(); } catch { }
            }
        };

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
            } else if ( e.KeyData == (Keys.Control | Keys.Alt | Keys.A) ) {
                TryOpenAdminPortal();
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
			tuioClient.removeTuioListener(this);

			tuioClient.disconnect();
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
            else if (o.SymbolID == TUIO_FAVORITE_TOGGLE_ID)
            {
                if (page == 5 && selectedArtifactId >= 0)
                {
                    ToggleFavoriteForSelectedArtifact();
                    Console.WriteLine("Favorite toggled by Marker 103");
                }
            }
            else if (o.SymbolID == ADMIN_AUTH_MARKER_ID)
            {
                Console.WriteLine("[ADMIN] Admin auth marker detected");
                TryOpenAdminPortal();
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
            else if (o.SymbolID == TUIO_FAVORITE_TOGGLE_ID)
            {
                return;
            }
            else if (o.SymbolID == ADMIN_AUTH_MARKER_ID)
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
                if (data == null) return null; // stream closed
                if (string.IsNullOrWhiteSpace(data)) return ""; // empty line — don't treat as disconnect
                Console.WriteLine(data);
                return data;
            }
            catch (System.IO.IOException)
            {
                return null; // genuine connection drop
            }
            catch (System.Exception ex)
            {
                Console.WriteLine("recieveMessage error: " + ex.Message);
                return null;
            }
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
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates = {
            Path.Combine(exeDir, @"..\..\artifacts.json"),
            Path.Combine(exeDir, @"artifacts.json"),
        };

        foreach (string path in candidates)
        {
            string fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                try
                {
                    string json = File.ReadAllText(fullPath);
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    ArtifactRoot root = serializer.Deserialize<ArtifactRoot>(json);
                    if (root != null && root.artifacts != null && root.artifacts.Count > 0)
                    {
                        artifacts = root.artifacts;
                        artifactsJsonPath = fullPath;
                        Console.WriteLine("Loaded artifacts from: " + fullPath + " (count=" + artifacts.Count + ")");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed loading artifacts from " + fullPath + ": " + ex.Message);
                }
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
                        // Preserve blank/empty as-is so ApplyUserTheme can apply
                        // the gender-default (e.g. female → pink). Only normalize
                        // values the user has actually chosen.
                        if (!string.IsNullOrWhiteSpace(user.themeMode))
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
        if (string.IsNullOrWhiteSpace(mode)) return "light";
        string m = mode.Trim().ToLowerInvariant();
        if (m == "dark") return "dark";
        if (m == "pink") return "pink";
        return "light";
    }

    private void ApplyThemeMode(string mode)
    {
        currentThemeMode = NormalizeThemeMode(mode);
        switch (currentThemeMode)
        {
            case "dark": currentTheme = darkTheme; break;
            case "pink": currentTheme = pinkTheme; break;
            default:     currentTheme = lightTheme; break;
        }

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
        // Default theme rule: female users with no explicit themeMode → pink.
        // Anyone else with no explicit themeMode → light. Explicit setting wins.
        string mode = user != null ? user.themeMode : null;
        if (string.IsNullOrWhiteSpace(mode))
        {
            string gender = user != null && user.gender != null ? user.gender.Trim().ToLowerInvariant() : "";
            mode = (gender == "female" || gender == "f") ? "pink" : "light";
        }
        ApplyThemeMode(mode);
    }

    // Apply the active age profile: notify Python about camera visibility,
    // optionally force a high-contrast theme, log the choice.
    private void ApplyAgeProfile()
    {
        if (activeProfile == null) activeProfile = ResolveAgeProfile(25);

        try
        {
            string cameraCmd = activeProfile.CameraVisible ? "CAMERA:ON" : "CAMERA:OFF";
            socketClient?.sendMessage(cameraCmd);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[UI] Failed to send CAMERA command: " + ex.Message);
        }

        if (activeProfile.ForceHighContrast)
        {
            ApplyThemeMode("dark");
        }

        // Re-tune emotion-effects engine for this mode (confirmation, cooldowns).
        try { emotionEngine.Configure(activeProfile.Mode, accentBrush.Color); } catch { }

        lock (transcriptionLock)
        {
            transcriptionLog.Clear();
            transcriptionLog.Add("[Adaptive UI] Mode: " + activeProfile.Label);
            transcriptionLog.Add("[Adaptive UI] Camera: " + (activeProfile.CameraVisible ? "on" : "off"));
        }

        Console.WriteLine("[UI] AgeProfile applied -> mode=" + activeProfile.Label
            + " camera=" + activeProfile.CameraVisible
            + " transcription=" + activeProfile.ShowTranscription
            + " fontScale=" + activeProfile.FontScale);
    }

    private void ToggleThemeMode()
    {
        // Cycle: light → dark → pink → light
        string nextMode;
        switch (currentThemeMode)
        {
            case "light": nextMode = "dark"; break;
            case "dark":  nextMode = "pink"; break;
            default:      nextMode = "light"; break;
        }
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
                artifactFavoriteHint = "Make a CIRCLE to toggle favorites!";
                page = 5;
                SendMarkerUpdate(markerId);
                SendArtifactFocus(artifact, "tuio");
                Invalidate();
            });
            return;
        }

        selectedArtifactId = artifact.id;
        artifactFavoriteHint = "Make a CIRCLE to toggle favorites!";
        page = 5;
        SendMarkerUpdate(markerId);
        SendArtifactFocus(artifact, "tuio");
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
        artifactFavoriteHint = "Make a CIRCLE to toggle favorites!";
        page = 5;
        SendArtifactFocus(artifact, "mouse");
        Invalidate();
    }

    void SendArtifactFocus(ArtifactRecord artifact, string source)
    {
        if (socketClient == null || artifact == null) return;

        try
        {
            var payload = new Dictionary<string, object>();
            payload["type"] = "artifact_focus";
            payload["artifact"] = artifact.name ?? "";
            payload["category"] = artifact.country ?? artifact.era ?? artifact.origin ?? "general";
            payload["id"] = artifact.id;
            payload["tuioId"] = artifact.tuioId;
            payload["source"] = source ?? "unknown";
            var serializer = new JavaScriptSerializer();
            socketClient.sendMessage(serializer.Serialize(payload));
        }
        catch (Exception)
        {
            // Ignore: socket may be disconnected or serializer may fail on unexpected data.
        }
    }

    void SendContextClear(string source)
    {
        if (socketClient == null) return;

        try
        {
            var payload = new Dictionary<string, object>();
            payload["type"] = "context_update";
            payload["clear"] = true;
            payload["source"] = source ?? "unknown";
            var serializer = new JavaScriptSerializer();
            socketClient.sendMessage(serializer.Serialize(payload));
        }
        catch (Exception)
        {
        }
    }

    private void TryOpenAdminPortal()
    {
        bool adminExists = false;
        foreach (UserRecord user in allUsers)
        {
            if (user == null)
            {
                continue;
            }

            string userRole = string.IsNullOrWhiteSpace(user.role) ? string.Empty : user.role.Trim().ToLowerInvariant();
            if (userRole == "admin")
            {
                adminExists = true;
                break;
            }
        }

        if (!adminExists)
        {
            MessageBox.Show(this, "No admin user found in users.json.", "Admin Authentication", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string artifactsPath = ResolveArtifactsPath();
        if (string.IsNullOrWhiteSpace(artifactsPath))
        {
            MessageBox.Show(this, "Could not resolve artifacts.json path for admin management.", "Admin Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string contextPath = ResolveContextDataPath();
        string reportsPath = ResolveReportsPath();

        using (var dashboard = new AdminDashboardForm(
            artifactsPath,
            contextPath,
            reportsPath,
            () =>
            {
                LoadArtifacts();
                Invalidate();
            }))
        {
            dashboard.ShowDialog(this);
        }
    }

    private bool RouteGestureToAdmin(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return false;

        if (InvokeRequired)
        {
            try
            {
                return (bool)Invoke(new Func<bool>(() => RouteGestureToAdmin(gesture)));
            }
            catch
            {
                return false;
            }
        }

        for (int i = Application.OpenForms.Count - 1; i >= 0; i--)
        {
            Form openForm = Application.OpenForms[i];
            IAdminGestureReceiver receiver = openForm as IAdminGestureReceiver;
            if (receiver == null || !openForm.Visible)
            {
                continue;
            }

            if (receiver.HandleGestureCommand(gesture))
            {
                return true;
            }
        }

        return false;
    }

    private UserRecord FindAdminUser(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        string normalizedUsername = username.Trim();
        foreach (UserRecord user in allUsers)
        {
            if (user == null)
            {
                continue;
            }

            string userRole = string.IsNullOrWhiteSpace(user.role) ? string.Empty : user.role.Trim().ToLowerInvariant();
            if (userRole != "admin")
            {
                continue;
            }

            if (string.Equals((user.name ?? string.Empty).Trim(), normalizedUsername, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((user.password ?? string.Empty).Trim(), password, StringComparison.Ordinal))
            {
                return user;
            }
        }

        return null;
    }

    private UserRecord FindAdminUserByPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        string candidatePassword = password.Trim();
        foreach (UserRecord user in allUsers)
        {
            if (user == null)
            {
                continue;
            }

            string userRole = string.IsNullOrWhiteSpace(user.role) ? string.Empty : user.role.Trim().ToLowerInvariant();
            if (userRole != "admin")
            {
                continue;
            }

            if (string.Equals(user.password ?? string.Empty, candidatePassword, StringComparison.Ordinal))
            {
                return user;
            }
        }

        return null;
    }

    private string ResolveArtifactsPath()
    {
        if (!string.IsNullOrWhiteSpace(artifactsJsonPath) && File.Exists(artifactsJsonPath))
        {
            return artifactsJsonPath;
        }

        string[] candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\artifacts.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\artifacts.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts.json")
        };

        foreach (string candidate in candidates)
        {
            string fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return string.Empty;
    }

    private string ResolveContextDataPath()
    {
        string[] candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\context_data.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\context_data.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "context_data.json")
        };

        foreach (string candidate in candidates)
        {
            string fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return string.Empty;
    }

    private string ResolveReportsPath()
    {
        string[] candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\reports"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\reports"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reports")
        };

        foreach (string candidate in candidates)
        {
            string fullPath = Path.GetFullPath(candidate);
            if (Directory.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return string.Empty;
    }

    void GoToPage(int pageIndex)
    {
        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                if (pageIndex == 3 || pageIndex == 6) { RefreshCurrentUserFromUsersFile(); favoritesPageIndex = 0; }
                page = pageIndex;
                if (pageIndex != 5)
                {
                    selectedArtifactId = -1;
                    SendContextClear($"page_{pageIndex}");
                }
                Invalidate();
            });
            return;
        }

        if (pageIndex == 3 || pageIndex == 6) { RefreshCurrentUserFromUsersFile(); favoritesPageIndex = 0; }
        page = pageIndex;
        if (pageIndex != 5)
        {
            selectedArtifactId = -1;
            SendContextClear($"page_{pageIndex}");
        }
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

    // Navigate to next artifact while on the artifact detail page (page 5)
    void NavigateNextArtifact()
    {
        if (artifacts == null || artifacts.Count == 0) return;
        int currentIndex = artifacts.FindIndex(a => a.id == selectedArtifactId);
        int nextIndex = (currentIndex + 1) % artifacts.Count;
        OpenArtifactDetails(artifacts[nextIndex].id);
    }

    // Navigate to previous artifact while on the artifact detail page (page 5)
    void NavigatePreviousArtifact()
    {
        if (artifacts == null || artifacts.Count == 0) return;
        int currentIndex = artifacts.FindIndex(a => a.id == selectedArtifactId);
        int prevIndex = (currentIndex - 1 + artifacts.Count) % artifacts.Count;
        OpenArtifactDetails(artifacts[prevIndex].id);
    }


    class LoginPayload
    {
        public string type { get; set; }
        public string name { get; set; }
        public string age { get; set; }
        public string gender { get; set; }
        public object mac { get; set; }   // can be string or array
        public string Profile { get; set; }
        public string themeMode { get; set; }
        public string error { get; set; }
        public string source { get; set; }
        public List<string> images { get; set; }
        public List<int> favorites { get; set; }
    }

    private bool TryHandleLoginPayload(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage) || !rawMessage.TrimStart().StartsWith("{"))
        {
            Console.WriteLine("[LOGIN] TryHandleLoginPayload: not a JSON object, skipping");
            return false;
        }

        try
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            LoginPayload payload = serializer.Deserialize<LoginPayload>(rawMessage);

            if (payload == null || !string.Equals(payload.type, "user_login", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[LOGIN] TryHandleLoginPayload: type='" + (payload?.type ?? "null") + "' — not user_login");
                return false;
            }

            Console.WriteLine("[LOGIN] TryHandleLoginPayload: matched user_login, name=" + payload.name);

            uname = string.IsNullOrWhiteSpace(payload.name) ? "Visitor" : payload.name.Trim();

            // Set current user
            currentUser = GetUserByName(uname);
            if (currentUser == null && payload.mac != null)
            {
                string macStr = payload.mac is string s ? s : null;
                if (!string.IsNullOrWhiteSpace(macStr))
                    currentUser = GetUserByMac(macStr);
            }

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
            
            // Resolve adaptive UI profile from age (payload or currentUser).
            int parsedAge = 25;
            string ageStr = !string.IsNullOrWhiteSpace(payload.age)
                ? payload.age
                : (currentUser != null ? currentUser.age : "25");
            if (!int.TryParse((ageStr ?? "").Trim(), out parsedAge)) parsedAge = 25;
            activeProfile = ResolveAgeProfile(parsedAge);

            // load the user's saved light/dark theme
            if (currentUser != null)
            {
                ApplyUserTheme(currentUser);
            }
            else
            {
                ApplyThemeMode(payload.themeMode);
            }

            // Push the adaptive profile (may also override the theme for Senior mode)
            ApplyAgeProfile();

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to parse login payload: " + ex.Message);
            return false;
        }
    }

	public void SafeInvalidate()
	{
		try { Invoke((Action)(Invalidate)); } catch { }
	}

     public void stream()
    {
		
        Client c = new Client();
        if (!c.connectToSocket("localhost", 5000))    
        {
            Console.WriteLine("Could not connect.");
            btStatus = "Vision Engine Offline";
            SafeInvalidate();
            return;
        }

        socketClient = c;
        
        cameraStatusStr = "Online";
        btStatus = "Waiting for Bluetooth Device...";
        SafeInvalidate();

        // Ensure the vision engine starts with no selected artifact until the user opens one.
        SendContextClear("startup");
        Console.WriteLine("[STREAM] Entering receive loop, login=" + login);
        
        while (true)
        {
            msg = c.recieveMessage();
            if (msg == null) // Connection dropped
            {
                Console.WriteLine("[STREAM] recieveMessage returned null — connection dropped");
                cameraStatusStr = "Offline";
                btStatus = "Vision Engine Offline";
                SafeInvalidate();
                break;
            }
            if (string.IsNullOrWhiteSpace(msg))
            {
                continue;
            }

            // Transcription stream from Python (system events: gestures, expressions, faces).
            // Always logged so it's there if the user later switches to a mode that shows it.
            if (msg.StartsWith("TRANS:", StringComparison.Ordinal))
            {
                string entry = msg.Substring("TRANS:".Length).Trim();
                if (entry.Length > 0)
                {
                    string stamped = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + entry;
                    lock (transcriptionLock)
                    {
                        transcriptionLog.Add(stamped);
                        while (transcriptionLog.Count > MAX_TRANSCRIPTION_LINES)
                            transcriptionLog.RemoveAt(0);
                    }

                    // Hook for the emotion-effects engine: parse expression events
                    // like "Expression: happy (gaze: center)" and route the emotion.
                    if (entry.StartsWith("Expression: ", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            string rest = entry.Substring("Expression: ".Length);
                            int paren = rest.IndexOf('(');
                            string emotion = (paren > 0 ? rest.Substring(0, paren) : rest).Trim();
                            emotionEngine.OnEmotionEvent(emotion);

                            // Extract gaze zone for the spotlight overlay.
                            // Format: "happy (gaze: center_center)" — pull what's after "gaze:".
                            int gIdx = rest.IndexOf("gaze:", StringComparison.OrdinalIgnoreCase);
                            if (gIdx >= 0)
                            {
                                int zStart = gIdx + 5;
                                int zEnd = rest.IndexOf(')', zStart);
                                string zone =
                                    (zEnd > zStart ? rest.Substring(zStart, zEnd - zStart) : rest.Substring(zStart))
                                    .Trim().ToLowerInvariant();
                                string[] parts = zone.Split('_');
                                string vPart = parts.Length == 2 ? parts[0] : "center";
                                string hPart = parts.Length == 2 ? parts[1] : "center";
                                string[] validH = { "left", "center", "right" };
                                string[] validV = { "top", "center", "bottom" };
                                if (Array.IndexOf(validH, hPart) >= 0 && Array.IndexOf(validV, vPart) >= 0)
                                {
                                    lastGazeZone = zone;
                                }
                            }
                            // If an effect was spawned (engine now has live effects), start
                            // the 30 fps repaint timer.
                            if (emotionEngine.HasActiveEffects)
                            {
                                try { effectsTimer.Start(); } catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[FX] Emotion routing failed: " + ex.Message);
                        }
                    }

                    try { Invoke((Action)Invalidate); } catch { }
                }
                continue;
            }

            Console.WriteLine("[STREAM] Received msg (login=" + login + "): " + msg.Substring(0, Math.Min(120, msg.Length)));

            lastGestureTime = DateTime.Now;
            if (msg == "q")
            {
                c.stream.Close();
                c.client.Close();
                Console.WriteLine("Connection Terminated !");
                break;
            }
            if(login==0)
            {
                Console.WriteLine("[STREAM] Attempting TryHandleLoginPayload...");
                if (TryHandleLoginPayload(msg))
                {
                    Console.WriteLine("[STREAM] Login payload handled successfully, uname=" + uname);
                    SafeInvalidate();
                    continue;
                }
                Console.WriteLine("[STREAM] TryHandleLoginPayload returned false");

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
                SafeInvalidate();

            }
           
            else
            {
                string gesture = msg.Trim();

                if (RouteGestureToAdmin(gesture))
                {
                    SafeInvalidate();
                    oldmsg = msg;
                    continue;
                }

                // SwipeRight: navigate next artifact (on detail page) or next menu page
                if (gesture == "SwipeRight")
                {
                    if (page == 5 && selectedArtifactId >= 0) NavigateNextArtifact();
                    else NavigateNextPage();
                }

                // SwipeLeft: navigate previous artifact (on detail page) or previous menu page
                if (gesture == "SwipeLeft")
                {
                    if (page == 5 && selectedArtifactId >= 0) NavigatePreviousArtifact();
                    else NavigatePreviousPage();
                }

                // Circle: toggle favourite for the currently open artifact
                if (gesture == "Circle" && page == 5 && selectedArtifactId >= 0)
                {
                    ToggleFavoriteForSelectedArtifact();
                }

                // Mute: toggle audio narration
                if (gesture == "Mute")
                {
                    ToggleNarration();
                }

                // DarkMode: toggle light/dark theme
                if (gesture == "DarkMode")
                {
                    ToggleThemeMode();
                }

                SafeInvalidate();
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
        
        // === Adaptive header sizing driven by the active age profile ===
        float fs = activeProfile != null ? activeProfile.FontScale : 1.0f;
        // Cap header chrome scaling so it doesn't overflow the fixed 105-px header
        // band on Senior mode (FontScale 1.55 was blowing the nav off-screen).
        float hdrFs = Math.Min(fs, 1.18f);
        bool bigIcons = activeProfile != null && activeProfile.LargeIcons;
        bool showStatusIndicators = activeProfile == null
            || (activeProfile.Mode != UIMode.Child && activeProfile.Mode != UIMode.Senior);
        bool showThemeToggle = activeProfile == null || activeProfile.Mode != UIMode.Child;

        // Draw Application Title (Child mode gets a shorter, friendlier label)
        string titleText = activeProfile != null && activeProfile.Mode == UIMode.Child
            ? "Museum Adventure!"
            : "Smart Egyptian Museum";
        Font titleFont = new Font("Segoe UI", 22f * hdrFs, FontStyle.Bold);
        g.DrawString(titleText, titleFont, fntBrush, 30, 22);

        Font headerNavFont = new Font("Segoe UI", 10f * hdrFs, FontStyle.Bold);
        string[] headerPages = { "Home", "Profile", "Artifacts", "Favourites", "Explore" };

        int headerNavX = bigIcons ? 340 : 310;
        int headerNavY = bigIcons ? 58 : 62;
        int headerNavW = bigIcons ? (int)(112 * hdrFs) : 98;
        int headerNavH = bigIcons ? (int)(34 * hdrFs)  : 28;
        int headerNavGap = bigIcons ? 10 : 10;

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
            g.DrawRectangle(isActivePage ? new Pen(accentBrush.Color, 2) : borderPen, tabRect);
            SizeF tabTextSize = g.MeasureString(headerPages[i], headerNavFont);
            g.DrawString(
                headerPages[i],
                headerNavFont,
                isActivePage ? accentBrush : fntBrush,
                tabRect.X + (tabRect.Width - tabTextSize.Width) / 2,
                tabRect.Y + (tabRect.Height - tabTextSize.Height) / 2
            );
            pageClickTargets.Add(new PageClickTarget { Bounds = tabRect, PageIndex = i });
        }

        // Draw User Status in Center (scaled with profile)
        if (uname != "Visitor")
        {
            Font userFont = new Font("Segoe UI", 14f * fs, FontStyle.Regular);
            string userText = "Welcome " + uname;
            SizeF userSize = g.MeasureString(userText, userFont);
            g.DrawString(userText, userFont, fntBrush, (this.ClientSize.Width - userSize.Width) / 2, 15);

            // Bluetooth line: hidden for Child mode (declutter), shown otherwise.
            if (activeProfile == null || activeProfile.Mode != UIMode.Child)
            {
                string btText = "Bluetooth Connected";
                Font btFont = new Font("Segoe UI", 12f * fs, FontStyle.Bold);
                SizeF btSize = g.MeasureString(btText, btFont);
                g.DrawString(btText, btFont, textLightBrush, (this.ClientSize.Width - btSize.Width) / 2, 45);
            }
        }

        // Draw System Status on Right — hidden in Child/Senior to reduce noise.
        if (showStatusIndicators)
        {
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
        }

        if (showThemeToggle)
        {
            Font statusFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            string themeLabel = currentThemeMode.Substring(0, 1).ToUpper() + currentThemeMode.Substring(1);
            SizeF themeSize = g.MeasureString(themeLabel + " mode", statusFont);
            int themeX = (this.ClientSize.Width - 250) - 145;
            Rectangle themeRect = new Rectangle(themeX, 20, (int)themeSize.Width + 30, 30);
            themeToggleButtonRect = themeRect;
            g.FillRectangle(blbBrush, themeRect);
            g.DrawRectangle(borderPen, themeRect);
            g.DrawString(themeLabel + " mode", statusFont, accentBrush, themeX + 15, 27);
        }

        // Draw Page Content
        int contentY = 120;

        if (uname == "Visitor" && page != 5)
        {
            // Modern login card: rounded, with a subtle gradient header strip,
            // a circular avatar slot, and a status pill at the bottom.
            int cw = 520, ch = 540;
            int cX = (this.ClientSize.Width - cw) / 2;
            int cY = (this.ClientSize.Height - ch) / 2;
            Rectangle card = new Rectangle(cX, cY, cw, ch);

            // Card body
            FillRoundedRect(g, cardBsh_dynamic, card, 22);
            DrawRoundedRect(g, borderPen, card, 22);

            // Header strip
            Rectangle header = new Rectangle(cX, cY, cw, 110);
            using (GraphicsPath topClip = BuildRoundedRectPath(card, 22))
            {
                Region oldClip = g.Clip;
                g.SetClip(topClip);
                using (var gb = new LinearGradientBrush(header,
                    accentBrush.Color, ControlPaint.Light(accentBrush.Color, 0.4f), 0f))
                {
                    g.FillRectangle(gb, header);
                }
                g.Clip = oldClip;
            }
            g.DrawString("Smart Museum",
                new Font("Segoe UI", 20f, FontStyle.Bold), Brushes.White, cX + 28, cY + 22);
            g.DrawString("Bluetooth Identity Check",
                new Font("Segoe UI", 11f, FontStyle.Regular),
                new SolidBrush(Color.FromArgb(220, 255, 255, 255)), cX + 28, cY + 58);

            // Avatar — overlapping the header
            int av = 132;
            Rectangle avR = new Rectangle(cX + (cw - av) / 2, cY + 110 - av / 2, av, av);
            FillRoundedRect(g, currentTheme.cardBackground, avR, av / 2);
            DrawRoundedRect(g, new Pen(currentTheme.border, 2), avR, av / 2);
            FillRoundedRect(g, avatarBrush, new Rectangle(avR.X + 4, avR.Y + 4, av - 8, av - 8), (av - 8) / 2);
            if (upic != null)
            {
                Region prev = g.Clip;
                using (GraphicsPath circle = new GraphicsPath())
                {
                    circle.AddEllipse(avR);
                    g.SetClip(circle);
                    g.DrawImage(upic, avR);
                }
                g.Clip = prev;
            }

            // "Hello, …"
            string greet = "Hello, " + uname;
            Font greetFont = new Font("Segoe UI", 22f, FontStyle.Bold);
            SizeF gs = g.MeasureString(greet, greetFont);
            g.DrawString(greet, greetFont, fntBrush,
                cX + (cw - gs.Width) / 2, avR.Bottom + 18);

            Font subFont = new Font("Segoe UI", 12f);
            string sub = "Verifying your device…";
            SizeF ss = g.MeasureString(sub, subFont);
            g.DrawString(sub, subFont, textLightBrush,
                cX + (cw - ss.Width) / 2, avR.Bottom + 58);

            // Status pill
            Font pillFont = new Font("Segoe UI", 11f, FontStyle.Bold);
            SizeF ps = g.MeasureString(btStatus, pillFont);
            int pillW = Math.Max(200, (int)ps.Width + 40);
            int pillH = 38;
            Rectangle pill = new Rectangle(cX + (cw - pillW) / 2, cY + ch - pillH - 36, pillW, pillH);
            FillRoundedRect(g, blbBrush, pill, pillH / 2);
            DrawStringCentered(g, btStatus, pillFont, accentBrush, pill);

        }
        else if (page == 0) // Home — dispatched per age mode
        {
            UIMode mode = activeProfile != null ? activeProfile.Mode : UIMode.Adult;
            switch (mode)
            {
                case UIMode.Child:  DrawHomeChild(g, contentY);  break;
                case UIMode.Teen:   DrawHomeTeen(g, contentY);   break;
                case UIMode.Senior: DrawHomeSenior(g, contentY); break;
                default:            DrawHomeAdult(g, contentY);  break;
            }
        }
        else if (page == 1) // Profile — dispatched per age mode
        {
            UIMode pmode = activeProfile != null ? activeProfile.Mode : UIMode.Adult;
            switch (pmode)
            {
                case UIMode.Child:  DrawProfileChild(g, contentY);  break;
                case UIMode.Senior: DrawProfileSenior(g, contentY); break;
                default:            DrawProfileDetailed(g, contentY); break;
            }
        }
        // (the Profile rendering body has been extracted into DrawProfileDetailed / Child / Senior)
        else if (page == 2) // Artifacts — dispatched per age mode
        {
            UIMode amode = activeProfile != null ? activeProfile.Mode : UIMode.Adult;
            if (amode == UIMode.Child)  { DrawArtifactsChild(g, contentY);  return; }
            if (amode == UIMode.Senior) { DrawArtifactsSenior(g, contentY); return; }
            // fall through to detailed (Teen + Adult) inline block below
            bool showRightPanel = activeProfile == null || activeProfile.CameraVisible;
            float fs2 = activeProfile != null ? activeProfile.FontScale : 1.0f;

            g.DrawString("All Artifacts",
                new Font("Segoe UI", 22f * fs2, FontStyle.Bold), fntBrush, 40, contentY);

            int rightPanelW = showRightPanel ? 360 : 0;
            int rightPanelX = this.ClientSize.Width - rightPanelW - 40;
            int rightPanelY = contentY;

            // Search bar
            Rectangle searchRect = new Rectangle(40, contentY + 38, 430, 34);
            g.FillRectangle(cardBsh_dynamic, searchRect);
            g.DrawRectangle(borderPen, searchRect);
            g.DrawString("Search by name, era, dynasty...", new Font("Segoe UI", 9.5f), textLightBrush, searchRect.X + 12, searchRect.Y + 9);

            // Filters row
            int filterY = contentY + 82;
            int filterX = 40;
            int filterW = 160;
            int filterH = 28;
            int filterGap = 12;
            g.DrawString("Era:", new Font("Segoe UI", 9f, FontStyle.Bold), fntBrush, filterX, filterY + 6);
            Rectangle eraRect = new Rectangle(filterX + 40, filterY, filterW, filterH);
            g.FillRectangle(cardBsh_dynamic, eraRect);
            g.DrawRectangle(borderPen, eraRect);
            g.DrawString("All Eras", new Font("Segoe UI", 9f), textLightBrush, eraRect.X + 8, eraRect.Y + 6);

            int dynX = eraRect.Right + filterGap + 40;
            g.DrawString("Dynasty:", new Font("Segoe UI", 9f, FontStyle.Bold), fntBrush, dynX - 55, filterY + 6);
            Rectangle dynRect = new Rectangle(dynX, filterY, filterW, filterH);
            g.FillRectangle(cardBsh_dynamic, dynRect);
            g.DrawRectangle(borderPen, dynRect);
            g.DrawString("All Dynasties", new Font("Segoe UI", 9f), textLightBrush, dynRect.X + 8, dynRect.Y + 6);

            int matX = dynRect.Right + filterGap + 40;
            g.DrawString("Material:", new Font("Segoe UI", 9f, FontStyle.Bold), fntBrush, matX - 58, filterY + 6);
            Rectangle matRect = new Rectangle(matX, filterY, filterW, filterH);
            g.FillRectangle(cardBsh_dynamic, matRect);
            g.DrawRectangle(borderPen, matRect);
            g.DrawString("All Materials", new Font("Segoe UI", 9f), textLightBrush, matRect.X + 8, matRect.Y + 6);

            // Right column - Live feed (hidden in modes that asked for no camera)
            int liveH = 260;
            Rectangle liveRect = Rectangle.Empty;
            if (showRightPanel)
            {
                liveRect = new Rectangle(rightPanelX, rightPanelY, rightPanelW, liveH);
                g.FillRectangle(cardBsh_dynamic, liveRect);
                g.DrawRectangle(borderPen, liveRect);
                g.DrawString("Live Feed", new Font("Segoe UI", 12f, FontStyle.Bold), fntBrush, liveRect.X + 12, liveRect.Y + 10);
                Rectangle liveInner = new Rectangle(liveRect.X + 12, liveRect.Y + 40, liveRect.Width - 24, liveRect.Height - 60);
                g.FillRectangle(bgrBrush, liveInner);
                g.DrawRectangle(borderPen, liveInner);
                g.DrawString("Camera preview", new Font("Segoe UI", 9f), textLightBrush, liveInner.X + 10, liveInner.Y + 10);
            }

            // Right column - Selected artifact details (only when right panel is shown)
            int detailsY = (showRightPanel ? liveRect.Bottom : rightPanelY) + 18;
            int detailsH = 260;
            Rectangle detailsRect = new Rectangle(rightPanelX, detailsY, rightPanelW, detailsH);
            ArtifactRecord selected = GetArtifactById(selectedArtifactId);
            if (selected == null && artifacts.Count > 0) selected = artifacts[0];
            if (showRightPanel)
            {
            g.FillRectangle(cardBsh_dynamic, detailsRect);
            g.DrawRectangle(borderPen, detailsRect);
            g.DrawString("Selected Artifact Details", new Font("Segoe UI", 12f, FontStyle.Bold), fntBrush, detailsRect.X + 12, detailsRect.Y + 10);

            if (selected != null)
            {
                string imagePath = ResolveArtifactAssetPath(selected.objPath);
                Rectangle imgRect = new Rectangle(detailsRect.X + 12, detailsRect.Y + 42, 110, 110);
                g.FillRectangle(bgrBrush, imgRect);
                g.DrawRectangle(borderPen, imgRect);
                if (File.Exists(imagePath))
                {
                    try
                    {
                        Image artifactImg = Image.FromFile(imagePath);
                        g.DrawImage(artifactImg, imgRect);
                        artifactImg.Dispose();
                    }
                    catch { }
                }
                g.DrawString(selected.name, new Font("Segoe UI", 10.5f, FontStyle.Bold), fntBrush, imgRect.Right + 10, imgRect.Y + 4);
                g.DrawString(selected.era, new Font("Segoe UI", 9f), textLightBrush, imgRect.Right + 10, imgRect.Y + 26);
                g.DrawString(selected.origin, new Font("Segoe UI", 9f), textLightBrush, imgRect.Right + 10, imgRect.Y + 46);
                Rectangle descRect = new Rectangle(imgRect.X, imgRect.Bottom + 10, detailsRect.Width - 24, 60);
                g.DrawString(selected.description, new Font("Segoe UI", 8.5f), textLightBrush, descRect);

                Rectangle btn1 = new Rectangle(detailsRect.X + 12, detailsRect.Bottom - 44, 140, 30);
                Rectangle btn2 = new Rectangle(detailsRect.X + 160, detailsRect.Bottom - 44, 170, 30);
                g.FillRectangle(blbBrush, btn1);
                g.DrawRectangle(borderPen, btn1);
                g.FillRectangle(blbBrush, btn2);
                g.DrawRectangle(borderPen, btn2);
                g.DrawString("Open 3D View", new Font("Segoe UI", 9f, FontStyle.Bold), accentBrush, btn1.X + 18, btn1.Y + 7);
                g.DrawString("Play Audio Guide", new Font("Segoe UI", 9f, FontStyle.Bold), accentBrush, btn2.X + 18, btn2.Y + 7);
            }
            } // end if (showRightPanel)

            // Artifacts grid — when the right panel is collapsed, take full width
            // and switch to bigger cards (better for Child + Senior modes).
            int gridStartX = 40;
            int gridStartY = contentY + 130;
            int gridW = showRightPanel ? (rightPanelX - 60) : (this.ClientSize.Width - 80);
            int gap = 16;
            int colsPerRow = showRightPanel ? 4 : (activeProfile != null && activeProfile.LargeIcons ? 3 : 5);
            int cardW = (gridW - (colsPerRow - 1) * gap) / colsPerRow;
            int cardH = (int)(230 * (activeProfile != null ? Math.Max(1.0f, activeProfile.FontScale) : 1.0f));
            int maxCardsToShow = showRightPanel ? 8 : (colsPerRow * 2);

            for (int i = 0; i < artifacts.Count && i < maxCardsToShow; i++)
            {
                ArtifactRecord artifact = artifacts[i];
                int col = i % colsPerRow;
                int row = i / colsPerRow;
                int x = gridStartX + col * (cardW + gap);
                int y = gridStartY + row * (cardH + gap);
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
                        g.DrawImage(artifactImg, x, y, cardW, cardH - 90);
                        artifactImg.Dispose();
                    }
                    catch { }
                }

                float cfs = activeProfile != null ? activeProfile.FontScale : 1.0f;
                g.DrawString(artifact.name, new Font("Segoe UI", 10.5f * cfs, FontStyle.Bold), fntBrush, x + 8, y + cardH - 82);
                g.DrawString(artifact.era, new Font("Segoe UI", 9f * cfs), textLightBrush, x + 8, y + cardH - 60);

                Rectangle viewBtn = new Rectangle(x + 8, y + cardH - 32, 60, 22);
                Rectangle listenBtn = new Rectangle(x + 74, y + cardH - 32, 60, 22);
                Rectangle favBtn = new Rectangle(x + 140, y + cardH - 32, cardW - 148, 22);
                g.DrawRectangle(borderPen, viewBtn);
                g.DrawRectangle(borderPen, listenBtn);
                g.DrawRectangle(borderPen, favBtn);
                g.DrawString("View", new Font("Segoe UI", 8f * cfs), fntBrush, viewBtn.X + 16, viewBtn.Y + 4);
                g.DrawString("Listen", new Font("Segoe UI", 8f * cfs), fntBrush, listenBtn.X + 10, listenBtn.Y + 4);
                g.DrawString("Add to Fav", new Font("Segoe UI", 8f * cfs), fntBrush, favBtn.X + 6, favBtn.Y + 4);
            }
        }
        else if (page == 3 || page == 6) // Favourites — dispatched per age mode
        {
            RefreshCurrentUserFromUsersFile();
            UIMode fmode = activeProfile != null ? activeProfile.Mode : UIMode.Adult;
            if (fmode == UIMode.Child)  { DrawFavouritesChild(g, contentY);  return; }
            if (fmode == UIMode.Senior) { DrawFavouritesSenior(g, contentY); return; }
            // fall through to detailed (Teen + Adult) inline block below
            g.DrawString("My Favourites", new Font("Segoe UI", 22f, FontStyle.Bold), fntBrush, 40, contentY);

            int summaryW = 240;
            int rightPanelW = 340;
            int summaryX = 40;
            int summaryY = contentY + 50;
            int listX = summaryX + summaryW + 20;
            int listW = this.ClientSize.Width - listX - rightPanelW - 60;
            int rightPanelX = this.ClientSize.Width - rightPanelW - 40;

            Rectangle summaryRect = new Rectangle(summaryX, summaryY, summaryW, 260);
            g.FillRectangle(cardBsh_dynamic, summaryRect);
            g.DrawRectangle(borderPen, summaryRect);
            g.DrawString("Summary", new Font("Segoe UI", 12f, FontStyle.Bold), fntBrush, summaryRect.X + 12, summaryRect.Y + 10);

            string lastViewed = "-";
            if (selectedArtifactId >= 0)
            {
                ArtifactRecord lastArtifact = GetArtifactById(selectedArtifactId);
                if (lastArtifact != null) lastViewed = lastArtifact.name;
            }
            int favCount = currentUser?.favorites != null ? currentUser.favorites.Count : 0;
            g.DrawString("Total favourites: " + favCount, new Font("Segoe UI", 10f), textLightBrush, summaryRect.X + 12, summaryRect.Y + 50);
            g.DrawString("Last viewed artifact:", new Font("Segoe UI", 10f), textLightBrush, summaryRect.X + 12, summaryRect.Y + 90);
            g.DrawString(lastViewed, new Font("Segoe UI", 10f, FontStyle.Bold), fntBrush, summaryRect.X + 12, summaryRect.Y + 115);

            Rectangle continueRect = new Rectangle(summaryRect.X + 12, summaryRect.Bottom - 48, summaryRect.Width - 24, 32);
            g.FillRectangle(blbBrush, continueRect);
            g.DrawRectangle(borderPen, continueRect);
            g.DrawString("Continue Tour", new Font("Segoe UI", 9f, FontStyle.Bold), accentBrush, continueRect.X + 38, continueRect.Y + 7);

            // Live feed panel
            Rectangle liveRect = new Rectangle(rightPanelX, summaryY, rightPanelW, 240);
            g.FillRectangle(cardBsh_dynamic, liveRect);
            g.DrawRectangle(borderPen, liveRect);
            g.DrawString("Live Feed", new Font("Segoe UI", 12f, FontStyle.Bold), fntBrush, liveRect.X + 12, liveRect.Y + 10);
            Rectangle liveInner = new Rectangle(liveRect.X + 12, liveRect.Y + 40, liveRect.Width - 24, liveRect.Height - 60);
            g.FillRectangle(bgrBrush, liveInner);
            g.DrawRectangle(borderPen, liveInner);

            // Gesture Recognition panel
            Rectangle gestureRect = new Rectangle(rightPanelX, liveRect.Bottom + 16, rightPanelW, 180);
            g.FillRectangle(cardBsh_dynamic, gestureRect);
            g.DrawRectangle(borderPen, gestureRect);
            g.DrawString("Gesture Recognition", new Font("Segoe UI", 12f, FontStyle.Bold), fntBrush, gestureRect.X + 12, gestureRect.Y + 10);
            g.DrawString("Circle = Open/Select", new Font("Segoe UI", 9f), textLightBrush, gestureRect.X + 12, gestureRect.Y + 45);
            g.DrawString("Swipe Left = Previous", new Font("Segoe UI", 9f), textLightBrush, gestureRect.X + 12, gestureRect.Y + 70);
            g.DrawString("Swipe Right = Next", new Font("Segoe UI", 9f), textLightBrush, gestureRect.X + 12, gestureRect.Y + 95);
            g.DrawString("MediaPipe: Tracking", new Font("Segoe UI", 9f, FontStyle.Bold), accentBrush, gestureRect.X + 12, gestureRect.Y + 125);

            if (currentUser == null || currentUser.favorites == null || currentUser.favorites.Count == 0)
            {
                g.DrawString("No favourites yet", new Font("Segoe UI", 14f), textLightBrush, listX, summaryY + 10);
            }
            else
            {
                List<ArtifactRecord> favoriteArtifacts = new List<ArtifactRecord>();
                foreach (int id in currentUser.favorites)
                {
                    ArtifactRecord artifact = GetArtifactById(id);
                    if (artifact != null) favoriteArtifacts.Add(artifact);
                }

                int itemH = 90;
                int itemW = listW;
                int startY = summaryY + 10;

                for (int i = 0; i < favoriteArtifacts.Count; i++)
                {
                    ArtifactRecord artifact = favoriteArtifacts[i];
                    int y = startY + i * (itemH + 10);
                    Rectangle itemRect = new Rectangle(listX, y, itemW, itemH);

                    g.FillRectangle(cardBsh_dynamic, itemRect);
                    g.DrawRectangle(borderPen, itemRect);
                    artifactClickTargets.Add(new ArtifactClickTarget { Bounds = itemRect, ArtifactId = artifact.id });

                    string imagePath = ResolveArtifactAssetPath(artifact.objPath);
                    if (File.Exists(imagePath))
                    {
                        try
                        {
                            Image artifactImage = Image.FromFile(imagePath);
                            g.DrawImage(artifactImage, itemRect.X + 10, itemRect.Y + 10, 70, 70);
                            artifactImage.Dispose();
                        }
                        catch { }
                    }

                    g.DrawString(artifact.name, new Font("Segoe UI", 11f, FontStyle.Bold), fntBrush, itemRect.X + 95, itemRect.Y + 16);
                    g.DrawString(artifact.era, new Font("Segoe UI", 9f), textLightBrush, itemRect.X + 95, itemRect.Y + 42);

                    Rectangle viewBtn = new Rectangle(itemRect.Right - 190, itemRect.Y + 28, 70, 26);
                    Rectangle audioBtn = new Rectangle(itemRect.Right - 110, itemRect.Y + 28, 70, 26);
                    g.DrawRectangle(borderPen, viewBtn);
                    g.DrawRectangle(borderPen, audioBtn);
                    g.DrawString("View 3D", new Font("Segoe UI", 8f), fntBrush, viewBtn.X + 8, viewBtn.Y + 6);
                    g.DrawString("Play", new Font("Segoe UI", 8f), fntBrush, audioBtn.X + 20, audioBtn.Y + 6);
                }
            }
        }
        else if (page == 4) // Explore — dispatched per age mode
        {
            UIMode emode = activeProfile != null ? activeProfile.Mode : UIMode.Adult;
            if (emode == UIMode.Child)  { DrawExploreChild(g, contentY);  return; }
            if (emode == UIMode.Senior) { DrawExploreSenior(g, contentY); return; }
            // fall through to detailed (Teen + Adult) inline block below
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
        else if (page == 5 && selectedArtifactId >= 0) // Details — dispatched per age mode
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

                UIMode dmode = activeProfile != null ? activeProfile.Mode : UIMode.Adult;
                if (dmode == UIMode.Child)  { DrawDetailsChild(g, contentY, artifact);  return; }
                if (dmode == UIMode.Senior) { DrawDetailsSenior(g, contentY, artifact); return; }
                // fall through to detailed (Teen + Adult) inline block below

                int startX = 40;
                int gutter = 20;
                int rightW = 360;
                int centerW = 360;
                int leftW = this.ClientSize.Width - startX * 2 - rightW - centerW - gutter * 2;
                if (leftW < 520) leftW = 520;

                int leftX = startX;
                int centerX = leftX + leftW + gutter;
                int rightX = centerX + centerW + gutter;

                // Left 3D Viewer Panel
                Rectangle viewerRect = new Rectangle(leftX, contentY, leftW, 320);
                g.FillRectangle(cardBsh_dynamic, viewerRect);
                g.DrawRectangle(borderPen, viewerRect);
                g.DrawString("3D Artifact Viewer", new Font("Segoe UI", 12f, FontStyle.Bold), fntBrush, viewerRect.X + 12, viewerRect.Y + 10);
                
                string gifPath = Resolve3DModelGifPath(artifact.name);
                if (gifPath != null)
                {
                    if (artifact3DPictureBox.ImageLocation != gifPath)
                    {
                        artifact3DPictureBox.ImageLocation = gifPath;
                        artifact3DPictureBox.LoadAsync();
                    }
                    artifact3DPictureBox.BackColor = currentTheme.cardBackground;
                    artifact3DPictureBox.Bounds = new Rectangle(viewerRect.X + 20, viewerRect.Y + 40, viewerRect.Width - 40, viewerRect.Height - 60);
                    if (!artifact3DPictureBox.Visible) artifact3DPictureBox.Visible = true;
                }
                else
                {
                    if (artifact3DPictureBox.Visible) artifact3DPictureBox.Visible = false;
                    string imagePath = ResolveArtifactAssetPath(artifact.objPath);
                    if (File.Exists(imagePath))
                    {
                        Image artifactImage = Image.FromFile(imagePath);
                        g.DrawImage(artifactImage, viewerRect.X + 20, viewerRect.Y + 40, viewerRect.Width - 40, viewerRect.Height - 60);
                        artifactImage.Dispose();
                    }
                }

                // Viewer controls
                Rectangle rotateBtn = new Rectangle(viewerRect.X + 20, viewerRect.Bottom + 10, 90, 26);
                Rectangle zoomBtn = new Rectangle(viewerRect.X + 120, viewerRect.Bottom + 10, 80, 26);
                Rectangle resetBtn = new Rectangle(viewerRect.X + 210, viewerRect.Bottom + 10, 80, 26);
                g.DrawRectangle(borderPen, rotateBtn);
                g.DrawRectangle(borderPen, zoomBtn);
                g.DrawRectangle(borderPen, resetBtn);
                g.DrawString("Rotate", new Font("Segoe UI", 8.5f), fntBrush, rotateBtn.X + 20, rotateBtn.Y + 6);
                g.DrawString("Zoom", new Font("Segoe UI", 8.5f), fntBrush, zoomBtn.X + 20, zoomBtn.Y + 6);
                g.DrawString("Reset", new Font("Segoe UI", 8.5f), fntBrush, resetBtn.X + 20, resetBtn.Y + 6);

                // Artifact voice panel
                Rectangle voiceRect = new Rectangle(leftX, viewerRect.Bottom + 46, leftW, 120);
                g.FillRectangle(cardBsh_dynamic, voiceRect);
                g.DrawRectangle(borderPen, voiceRect);
                g.DrawString("Artifact Voice", new Font("Segoe UI", 11f, FontStyle.Bold), fntBrush, voiceRect.X + 12, voiceRect.Y + 10);
                g.DrawString("Playback", new Font("Segoe UI", 9f), textLightBrush, voiceRect.X + 12, voiceRect.Y + 38);
                g.DrawLine(borderPen, voiceRect.X + 90, voiceRect.Y + 60, voiceRect.Right - 20, voiceRect.Y + 60);
                
                // Center Metadata Panel
                Rectangle metaRect = new Rectangle(centerX, contentY, centerW, 440);
                g.FillRectangle(cardBsh_dynamic, metaRect);
                g.DrawRectangle(borderPen, metaRect);
                g.DrawString("Artifact Metadata", new Font("Segoe UI", 12f, FontStyle.Bold), fntBrush, metaRect.X + 12, metaRect.Y + 10);
                
                Font keyFont = new Font("Segoe UI", 12f, FontStyle.Bold);
                Font valFont = new Font("Segoe UI", 12f);
                int lineY = metaRect.Y + 42;

                g.DrawString("Name:", keyFont, fntBrush, metaRect.X + 12, lineY);
                g.DrawString(artifact.name, valFont, fntBrush, metaRect.X + 120, lineY);
                lineY += 30;

                g.DrawString("Era:", keyFont, fntBrush, metaRect.X + 12, lineY);
                g.DrawString(artifact.era, valFont, fntBrush, metaRect.X + 120, lineY);
                lineY += 30;

                g.DrawString("Origin:", keyFont, fntBrush, metaRect.X + 12, lineY);
                g.DrawString(artifact.origin, valFont, fntBrush, metaRect.X + 120, lineY);
                lineY += 40;
                
                bool hasAudio = ResolveAudioPath(artifact.audioPath) != null;
                bool has3D = Resolve3DModelPath(artifact.name) != null;
                
                g.DrawString("3D Model:", keyFont, fntBrush, metaRect.X + 12, lineY);
                g.DrawString(has3D ? "Available" : "Coming soon", valFont, has3D ? accentBrush : textLightBrush, metaRect.X + 120, lineY);
                lineY += 30;
                
                g.DrawString("Audio:", keyFont, fntBrush, metaRect.X + 12, lineY);
                g.DrawString(hasAudio ? "Playing now" : "Coming soon", valFont, hasAudio ? accentBrush : textLightBrush, metaRect.X + 120, lineY);
                lineY += 40;

                audioToggleButtonRect = new Rectangle(metaRect.X + 12, lineY, 160, 30);
                g.FillRectangle(blbBrush, audioToggleButtonRect);
                g.DrawRectangle(borderPen, audioToggleButtonRect);
                g.DrawString(audioMuted ? "Unmute" : "Mute", new Font("Segoe UI", 9f, FontStyle.Bold), accentBrush, audioToggleButtonRect.X + 45, audioToggleButtonRect.Y + 6);
                lineY += 54;

                bool isFavorite = IsFavoriteArtifact(artifact.id);
                favoriteToggleButtonRect = new Rectangle(metaRect.X + 190, lineY - 54, 160, 30);
                g.FillRectangle(blbBrush, favoriteToggleButtonRect);
                g.DrawRectangle(borderPen, favoriteToggleButtonRect);
                g.DrawString(isFavorite ? "Remove" : "Add", new Font("Segoe UI", 9f, FontStyle.Bold), accentBrush, favoriteToggleButtonRect.X + 55, favoriteToggleButtonRect.Y + 6);

                g.DrawString("Description:", keyFont, fntBrush, metaRect.X + 12, lineY);
                RectangleF descRect = new RectangleF(metaRect.X + 12, lineY + 26, metaRect.Width - 24, 200);
                g.DrawString(artifact.description, valFont, textLightBrush, descRect);

                // Right column - Live feed
                Rectangle liveRect = new Rectangle(rightX, contentY, rightW, 260);
                g.FillRectangle(cardBsh_dynamic, liveRect);
                g.DrawRectangle(borderPen, liveRect);
                g.DrawString("Live Feed", new Font("Segoe UI", 12f, FontStyle.Bold), fntBrush, liveRect.X + 12, liveRect.Y + 10);
                Rectangle liveInner = new Rectangle(liveRect.X + 12, liveRect.Y + 40, liveRect.Width - 24, liveRect.Height - 60);
                g.FillRectangle(bgrBrush, liveInner);
                g.DrawRectangle(borderPen, liveInner);

                // Right column - Gesture Recognition
                Rectangle gestureRect = new Rectangle(rightX, liveRect.Bottom + 18, rightW, 180);
                g.FillRectangle(cardBsh_dynamic, gestureRect);
                g.DrawRectangle(borderPen, gestureRect);
                g.DrawString("Gesture Recognition", new Font("Segoe UI", 12f, FontStyle.Bold), fntBrush, gestureRect.X + 12, gestureRect.Y + 10);
                g.DrawString("Circle = Open/Select", new Font("Segoe UI", 9f), textLightBrush, gestureRect.X + 12, gestureRect.Y + 45);
                g.DrawString("Swipe Left = Previous", new Font("Segoe UI", 9f), textLightBrush, gestureRect.X + 12, gestureRect.Y + 70);
                g.DrawString("Swipe Right = Next", new Font("Segoe UI", 9f), textLightBrush, gestureRect.X + 12, gestureRect.Y + 95);
                g.DrawString("MediaPipe: Tracking", new Font("Segoe UI", 9f, FontStyle.Bold), accentBrush, gestureRect.X + 12, gestureRect.Y + 125);

                g.DrawString(artifactFavoriteHint, new Font("Segoe UI", 11f, FontStyle.Bold), accentBrush, centerX, contentY + 455);
            }
        }
        


        // Draw Navigation hint
        g.DrawString("Circle gesture: Open Menu   |   Swipe Left/Right: Navigate", new Font("Segoe UI", 11f, FontStyle.Italic), textLightBrush, 40, this.ClientSize.Height - 40);
        
        // Removed TUIO debug drawing for objects, cursors, and blobs to keep UI clean.

        // Draw the circular menu
        DrawCircularMenu(g, this.ClientSize.Width, this.ClientSize.Height);

        // === Adaptive overlays driven by the active age profile ===
        DrawAdaptiveOverlays(g);
    }

    // Renders age-profile-dependent UI: mode badge (top-right), transcription
    // panel (bottom-right), and a large accessible caption for Senior mode.
    private void DrawAdaptiveOverlays(Graphics g)
    {
        if (activeProfile == null) return;

        // (0) Gaze spotlight — rendered first so the badge / transcription
        // panel / effects sit on top of it.
        DrawGazeSpotlight(g);

        // (1) Profile badge — small chip below the status block, top-right.
        try
        {
            string badge = "Mode: " + activeProfile.Label;
            Font badgeFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            SizeF badgeSize = g.MeasureString(badge, badgeFont);
            int bx = this.ClientSize.Width - (int)badgeSize.Width - 30;
            int by = 100 - (int)badgeSize.Height - 4;
            Rectangle badgeRect = new Rectangle(bx - 8, by - 2,
                (int)badgeSize.Width + 16, (int)badgeSize.Height + 6);
            g.FillRectangle(blbBrush, badgeRect);
            g.DrawRectangle(borderPen, badgeRect);
            g.DrawString(badge, badgeFont, accentBrush, bx, by);
        }
        catch { }

        // (2) Transcription panel — only when the profile asks for it.
        // For Senior we leave more bottom margin so it doesn't collide with the
        // big caption strip; for Adult/Teen it tucks under the bottom navigation.
        if (activeProfile.ShowTranscription)
        {
            try
            {
                bool isSenior = activeProfile.Mode == UIMode.Senior;
                int panelW = 340;
                int panelH = isSenior ? 230 : 200;
                int bottomReserve = isSenior ? 80 : 55;
                int panelX = this.ClientSize.Width - panelW - 20;
                int panelY = this.ClientSize.Height - panelH - bottomReserve;

                Rectangle panelRect = new Rectangle(panelX, panelY, panelW, panelH);
                FillRoundedRect(g, cardBsh_dynamic, panelRect, 14);
                DrawRoundedRect(g, borderPen, panelRect, 14);

                // Cap font multiplier inside the panel so lines don't run off.
                float pfs = Math.Min(activeProfile.FontScale, 1.25f);
                float titleSize = 11f * pfs;
                float lineSize  = 9f  * pfs;
                Font titleFont = new Font("Segoe UI", titleSize, FontStyle.Bold);
                Font lineFont  = new Font("Segoe UI", lineSize,  FontStyle.Regular);

                g.DrawString("Live Transcription", titleFont, accentBrush,
                    panelX + 14, panelY + 10);
                g.DrawLine(borderPen, panelX + 14, panelY + 34,
                    panelX + panelW - 14, panelY + 34);

                List<string> snapshot;
                lock (transcriptionLock) { snapshot = new List<string>(transcriptionLog); }

                int lineY = panelY + 42;
                int lineHeight = (int)(lineSize * 2.2f) + 2;
                int maxLines = Math.Max(1, (panelH - 56) / lineHeight);
                int start = Math.Max(0, snapshot.Count - maxLines);
                for (int i = start; i < snapshot.Count; i++)
                {
                    string line = snapshot[i];
                    if (line.Length > 56) line = line.Substring(0, 53) + "…";
                    g.DrawString(line, lineFont, fntBrush, panelX + 14, lineY);
                    lineY += lineHeight;
                }
            }
            catch { }
        }

        // (3) Senior mode "new idea": persistent big-text caption strip across
        // the bottom of the screen showing the currently focused artifact.
        if (activeProfile.Mode == UIMode.Senior)
        {
            try
            {
                string caption = selectedArtifactId >= 0
                    ? ("Now viewing: " + GetArtifactNameById(selectedArtifactId))
                    : "Place a marker or open an artifact to learn more.";
                int stripH = 50;
                Rectangle stripRect = new Rectangle(0,
                    this.ClientSize.Height - stripH, this.ClientSize.Width, stripH);
                g.FillRectangle(blbBrush, stripRect);
                // Capped scale so text never overflows the 50px strip.
                Font capFont = new Font("Segoe UI", 16f, FontStyle.Bold);
                SizeF capSize = g.MeasureString(caption, capFont);
                if (capSize.Width > this.ClientSize.Width - 40)
                {
                    int maxChars = Math.Max(20, (int)((this.ClientSize.Width - 40) / (capSize.Width / Math.Max(1, caption.Length))));
                    if (caption.Length > maxChars) caption = caption.Substring(0, maxChars - 1) + "…";
                    capSize = g.MeasureString(caption, capFont);
                }
                g.DrawString(caption, capFont, accentBrush,
                    (this.ClientSize.Width - capSize.Width) / 2,
                    stripRect.Y + (stripH - capSize.Height) / 2);
            }
            catch { }
        }

        // (4) Emotion-reactive effects (balloons, ring, drops, calming overlay,
        // toasts). Drawn last so they sit on top of every other overlay.
        try
        {
            emotionEngine.DrawAll(g, new Rectangle(0, 0, this.ClientSize.Width, this.ClientSize.Height));
        }
        catch { }
    }

    private string GetArtifactNameById(int id)
    {
        foreach (var a in artifacts) if (a.id == id) return a.name ?? ("#" + id);
        return "#" + id;
    }

    // Soft radial glow that follows the user's gaze across a 3×3 grid.
    // Eased X/Y interpolation per paint, fade-in alpha, sits below all other
    // overlays so the badge / transcription / effects remain legible.
    private void DrawGazeSpotlight(Graphics g)
    {
        if (string.IsNullOrEmpty(lastGazeZone)) return;
        if (uname == "Visitor") return;

        int W = this.ClientSize.Width;
        int H = this.ClientSize.Height;

        // Parse zone: "top_left", "center_center", "bottom_right", etc.
        string[] parts = lastGazeZone.Split('_');
        string vPart = parts.Length == 2 ? parts[0] : "center";
        string hPart = parts.Length == 2 ? parts[1] : "center";

        // Target X: left = 1/6, center = 1/2, right = 5/6
        switch (hPart)
        {
            case "left":   gazeSpotlightTargetX = W / 6f; break;
            case "right":  gazeSpotlightTargetX = 5 * W / 6f; break;
            default:       gazeSpotlightTargetX = W / 2f; break;
        }
        // Target Y: top = 1/4, center = 1/2, bottom = 3/4
        // (offset down by 40px so the glow sits over content rather than
        //  the header band).
        switch (vPart)
        {
            case "top":    gazeSpotlightTargetY = (int)(H * 0.25f) + 40; break;
            case "bottom": gazeSpotlightTargetY = (int)(H * 0.75f) + 40; break;
            default:       gazeSpotlightTargetY = (int)(H * 0.50f) + 40; break;
        }

        // Snap directly to target (no easing — gaze needs to be instant).
        gazeSpotlightX = gazeSpotlightTargetX;
        gazeSpotlightY = gazeSpotlightTargetY;

        // Fade in over ~20 paints.
        gazeSpotlightAlpha = Math.Min(1.0f, gazeSpotlightAlpha + 0.05f);

        // Per-age intensity.
        int peakAlpha;
        float radiusFactor;
        switch (activeProfile != null ? activeProfile.Mode : UIMode.Adult)
        {
            case UIMode.Child:  peakAlpha = 180; radiusFactor = 0.34f; break;
            case UIMode.Senior: peakAlpha = 100; radiusFactor = 0.28f; break;
            default:            peakAlpha = 145; radiusFactor = 0.30f; break;
        }

        int radius = (int)(Math.Min(W, H) * radiusFactor);
        int cx = (int)gazeSpotlightX;
        int cy = (int)gazeSpotlightY;

        Rectangle rect = new Rectangle(cx - radius, cy - radius, radius * 2, radius * 2);

        // GDI+ radial gradient
        try
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                using (PathGradientBrush pgb = new PathGradientBrush(path))
                {
                    pgb.CenterPoint = new PointF(cx, cy);
                    int centreAlphaInt = (int)(peakAlpha * gazeSpotlightAlpha);
                    pgb.CenterColor = Color.FromArgb(centreAlphaInt, accentBrush.Color);
                    pgb.SurroundColors = new[] { Color.FromArgb(0, accentBrush.Color) };
                    pgb.FocusScales = new PointF(0.0f, 0.0f);
                    SmoothingMode prev = g.SmoothingMode;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    try { g.FillPath(pgb, path); }
                    finally { g.SmoothingMode = prev; }
                }
            }
        }
        catch { }
    }

    // =====================================================================
    //                        HOME PAGE — per-age layouts
    // =====================================================================

    // -------------- CHILD (≤12): playful 2x2 tile dashboard --------------
    // Big rounded coloured tiles, friendly hero banner, no jargon.
    private void DrawHomeChild(Graphics g, int contentY)
    {
        int W = this.ClientSize.Width;
        int padX = 40;
        int innerW = W - padX * 2;

        // Hero banner — bright gradient with a giant friendly title
        Rectangle hero = new Rectangle(padX, contentY, innerW, 130);
        FillRoundedGradient(g, hero,
            Color.FromArgb(255, 184, 76),  // sun-orange
            Color.FromArgb(255, 110, 175), // hot-pink
            22);
        Font heroTitle = new Font("Segoe UI", 30f, FontStyle.Bold);
        Font heroSub   = new Font("Segoe UI", 16f, FontStyle.Bold);
        string hi = "Hi " + uname + "! 👋";
        g.DrawString(hi, heroTitle, Brushes.White, hero.X + 28, hero.Y + 22);
        g.DrawString("Pick a tile to start your adventure", heroSub,
            Brushes.White, hero.X + 30, hero.Y + 78);

        // 2×2 tile grid with mascot-style colour per tile
        int gridTop = hero.Bottom + 24;
        int gridGap = 22;
        int tileW = (innerW - gridGap) / 2;
        int tileH = (this.ClientSize.Height - gridTop - 90) / 2 - gridGap / 2;
        var tiles = new[] {
            new { Label="Explore",     Sub="See artifacts",     Emoji="🏺", Col=Color.FromArgb(98, 199, 246), Page=2 },
            new { Label="Profile",     Sub="That's you!",       Emoji="🙂", Col=Color.FromArgb(255, 165, 102), Page=1 },
            new { Label="My Stars",    Sub="What you love",     Emoji="⭐", Col=Color.FromArgb(255, 217, 86),  Page=3 },
            new { Label="Discover",    Sub="Surprise me",       Emoji="🎲", Col=Color.FromArgb(160, 220, 130), Page=4 },
        };
        Font tileTitle = new Font("Segoe UI", 22f, FontStyle.Bold);
        Font tileSub   = new Font("Segoe UI", 13f, FontStyle.Regular);
        Font emojiFont = new Font("Segoe UI Emoji", 56f);

        for (int i = 0; i < tiles.Length; i++)
        {
            int col = i % 2, row = i / 2;
            int x = padX + col * (tileW + gridGap);
            int y = gridTop + row * (tileH + gridGap);
            Rectangle r = new Rectangle(x, y, tileW, tileH);

            FillRoundedRect(g, tiles[i].Col, r, 28);
            using (var shade = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
                FillRoundedRect(g, shade, new Rectangle(r.X, r.Y, r.Width, r.Height / 2), 28);

            // Emoji on the right
            SizeF em = g.MeasureString(tiles[i].Emoji, emojiFont);
            g.DrawString(tiles[i].Emoji, emojiFont, Brushes.White,
                r.Right - em.Width - 30, r.Y + (r.Height - em.Height) / 2);

            // Labels on the left
            g.DrawString(tiles[i].Label, tileTitle, Brushes.White, r.X + 32, r.Y + 30);
            g.DrawString(tiles[i].Sub,   tileSub,   new SolidBrush(Color.FromArgb(230, 255, 255, 255)),
                r.X + 32, r.Y + 70);

            pageClickTargets.Add(new PageClickTarget { Bounds = r, PageIndex = tiles[i].Page });
        }
    }

    // -------------- TEEN (13–19): showy dashboard, hero + carousel --------------
    private void DrawHomeTeen(Graphics g, int contentY)
    {
        int W = this.ClientSize.Width;
        int padX = 40;
        int innerW = W - padX * 2;

        // Hero strip with featured artifact on the left, stats on the right
        int heroH = 220;
        Rectangle hero = new Rectangle(padX, contentY, innerW, heroH);
        FillRoundedRect(g, cardBsh_dynamic, hero, 18);
        DrawRoundedRect(g, borderPen, hero, 18);

        ArtifactRecord featured = (artifacts != null && artifacts.Count > 0) ? artifacts[0] : null;
        Rectangle heroImg = new Rectangle(hero.X + 14, hero.Y + 14, 320, heroH - 28);
        DrawArtifactImageRounded(g, featured, heroImg, 14, currentTheme.avatarBackground);

        int infoX = heroImg.Right + 26;
        g.DrawString("FEATURED TODAY",
            new Font("Segoe UI", 10f, FontStyle.Bold), accentBrush, infoX, hero.Y + 18);
        g.DrawString(featured != null ? featured.name : "Loading…",
            new Font("Segoe UI", 26f, FontStyle.Bold), fntBrush, infoX, hero.Y + 38);
        g.DrawString(featured != null ? (featured.era + "  •  " + featured.origin) : "",
            new Font("Segoe UI", 12f, FontStyle.Regular), textLightBrush, infoX, hero.Y + 80);

        // CTA buttons
        Rectangle btnOpen = new Rectangle(infoX, hero.Bottom - 64, 160, 44);
        FillRoundedRect(g, accentBrush.Color, btnOpen, 22);
        DrawStringCentered(g, "Open Details", new Font("Segoe UI", 11f, FontStyle.Bold), Brushes.White, btnOpen);
        if (featured != null)
            artifactClickTargets.Add(new ArtifactClickTarget { Bounds = btnOpen, ArtifactId = featured.id });

        Rectangle btnExplore = new Rectangle(btnOpen.Right + 12, hero.Bottom - 64, 130, 44);
        FillRoundedRect(g, blbBrush, btnExplore, 22);
        DrawRoundedRect(g, borderPen, btnExplore, 22);
        DrawStringCentered(g, "All Artifacts", new Font("Segoe UI", 11f, FontStyle.Bold), accentBrush, btnExplore);
        pageClickTargets.Add(new PageClickTarget { Bounds = btnExplore, PageIndex = 2 });

        // Stat strip below
        int statY = hero.Bottom + 18;
        int statH = 110;
        int statGap = 16;
        int statW = (innerW - statGap * 3) / 4;
        int favCount = currentUser != null && currentUser.favorites != null ? currentUser.favorites.Count : 0;
        var stats = new[] {
            new { Top="Visited",   Big = (artifacts != null ? Math.Min(artifacts.Count, 3) : 0).ToString(),  Sub="artifacts" },
            new { Top="Favourites",Big = favCount.ToString(),         Sub="saved" },
            new { Top="Mode",      Big = (activeProfile != null ? activeProfile.Mode.ToString() : "Teen"), Sub="adaptive" },
            new { Top="Theme",     Big = currentThemeMode.Substring(0,1).ToUpper()+currentThemeMode.Substring(1), Sub="palette" },
        };
        for (int i = 0; i < stats.Length; i++)
        {
            Rectangle r = new Rectangle(padX + i * (statW + statGap), statY, statW, statH);
            FillRoundedRect(g, cardBsh_dynamic, r, 14);
            DrawRoundedRect(g, borderPen, r, 14);
            g.DrawString(stats[i].Top, new Font("Segoe UI", 10f, FontStyle.Bold), textLightBrush, r.X + 16, r.Y + 14);
            g.DrawString(stats[i].Big, new Font("Segoe UI", 22f, FontStyle.Bold), fntBrush, r.X + 16, r.Y + 36);
            g.DrawString(stats[i].Sub, new Font("Segoe UI", 10f), textLightBrush, r.X + 16, r.Y + 76);
        }

        // Horizontal carousel of next artifacts
        int carY = statY + statH + 20;
        g.DrawString("Discover More",
            new Font("Segoe UI", 16f, FontStyle.Bold), fntBrush, padX, carY);
        int carItemW = 200, carItemH = 150, carGap = 14;
        int carItems = Math.Min(artifacts != null ? artifacts.Count : 0, 5);
        for (int i = 0; i < carItems; i++)
        {
            ArtifactRecord a = artifacts[i];
            Rectangle r = new Rectangle(padX + i * (carItemW + carGap), carY + 30, carItemW, carItemH);
            FillRoundedRect(g, cardBsh_dynamic, r, 14);
            DrawRoundedRect(g, borderPen, r, 14);
            Rectangle imgR = new Rectangle(r.X + 8, r.Y + 8, r.Width - 16, r.Height - 50);
            DrawArtifactImageRounded(g, a, imgR, 10, currentTheme.avatarBackground);
            g.DrawString(a.name ?? ("Artifact " + a.id),
                new Font("Segoe UI", 10f, FontStyle.Bold), fntBrush, r.X + 12, r.Bottom - 36);
            artifactClickTargets.Add(new ArtifactClickTarget { Bounds = r, ArtifactId = a.id });
        }
    }

    // -------------- ADULT (20–49): dense, professional dashboard --------------
    private void DrawHomeAdult(Graphics g, int contentY)
    {
        int W = this.ClientSize.Width;
        int padX = 40;
        int innerW = W - padX * 2;

        // Compact title + sub
        g.DrawString("Smart Egyptian Museum",
            new Font("Segoe UI", 22f, FontStyle.Bold), fntBrush, padX, contentY);
        g.DrawString("Adaptive interface · " + (activeProfile != null ? activeProfile.Label : "Adult"),
            new Font("Segoe UI", 11f, FontStyle.Italic), textLightBrush, padX, contentY + 34);

        // KPI strip — 4 narrow cards
        int kpiY = contentY + 64;
        int kpiH = 86;
        int kpiGap = 14;
        int kpiW = (innerW - kpiGap * 3) / 4;
        int favCount = currentUser != null && currentUser.favorites != null ? currentUser.favorites.Count : 0;
        var kpis = new[] {
            new { K="Artifacts", V = (artifacts != null ? artifacts.Count.ToString() : "0"), Acc=true  },
            new { K="My Favs",   V = favCount.ToString(),  Acc=false },
            new { K="Theme",     V = currentThemeMode.Substring(0,1).ToUpper()+currentThemeMode.Substring(1), Acc=false },
            new { K="UI Mode",   V = (activeProfile != null ? activeProfile.Mode.ToString() : "Adult"), Acc=true  },
        };
        for (int i = 0; i < kpis.Length; i++)
        {
            Rectangle r = new Rectangle(padX + i * (kpiW + kpiGap), kpiY, kpiW, kpiH);
            FillRoundedRect(g, cardBsh_dynamic, r, 12);
            DrawRoundedRect(g, borderPen, r, 12);
            // Accent stripe on the left for "Acc" cards
            if (kpis[i].Acc)
                FillRoundedRect(g, accentBrush.Color, new Rectangle(r.X, r.Y, 5, r.Height), 4);
            g.DrawString(kpis[i].K, new Font("Segoe UI", 10f, FontStyle.Bold),
                textLightBrush, r.X + 18, r.Y + 14);
            g.DrawString(kpis[i].V, new Font("Segoe UI", 20f, FontStyle.Bold),
                fntBrush, r.X + 18, r.Y + 36);
        }

        // Two-column layout: 6-up artifact grid on the left, activity log on the right
        int splitX = padX + innerW - 320;
        int gridX = padX;
        int gridW = splitX - padX - 18;
        int rowY = kpiY + kpiH + 22;

        g.DrawString("Browse Collection",
            new Font("Segoe UI", 14f, FontStyle.Bold), fntBrush, gridX, rowY);
        int cardW = (gridW - 16) / 3;
        int cardH = 170;
        int gridStart = rowY + 28;
        int n = Math.Min(artifacts != null ? artifacts.Count : 0, 6);
        for (int i = 0; i < n; i++)
        {
            ArtifactRecord a = artifacts[i];
            int col = i % 3, row = i / 3;
            Rectangle r = new Rectangle(gridX + col * (cardW + 8),
                gridStart + row * (cardH + 8), cardW, cardH);
            FillRoundedRect(g, cardBsh_dynamic, r, 10);
            DrawRoundedRect(g, borderPen, r, 10);
            Rectangle imgR = new Rectangle(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 46);
            DrawArtifactImageRounded(g, a, imgR, 8, currentTheme.avatarBackground);
            g.DrawString(a.name ?? ("Artifact " + a.id),
                new Font("Segoe UI", 10.5f, FontStyle.Bold), fntBrush, r.X + 10, r.Bottom - 34);
            g.DrawString(a.era ?? "",
                new Font("Segoe UI", 9f), textLightBrush, r.X + 10, r.Bottom - 18);
            artifactClickTargets.Add(new ArtifactClickTarget { Bounds = r, ArtifactId = a.id });
        }

        // Activity panel (mirrors the transcription stream)
        Rectangle act = new Rectangle(splitX, rowY, 320,
            Math.Min(390, this.ClientSize.Height - rowY - 70));
        FillRoundedRect(g, cardBsh_dynamic, act, 12);
        DrawRoundedRect(g, borderPen, act, 12);
        g.DrawString("Recent Activity",
            new Font("Segoe UI", 13f, FontStyle.Bold), fntBrush, act.X + 16, act.Y + 14);
        g.DrawLine(borderPen, act.X + 12, act.Y + 40, act.Right - 12, act.Y + 40);

        List<string> snap;
        lock (transcriptionLock) { snap = new List<string>(transcriptionLog); }
        int lh = 22; int ly = act.Y + 50;
        Font logFont = new Font("Segoe UI", 9.5f);
        int max = Math.Max(1, (act.Height - 50) / lh);
        int start = Math.Max(0, snap.Count - max);
        for (int i = start; i < snap.Count; i++)
        {
            string s = snap[i];
            if (s.Length > 38) s = s.Substring(0, 35) + "…";
            g.DrawString(s, logFont, fntBrush, act.X + 16, ly);
            ly += lh;
        }
    }

    // -------------- SENIOR (50+): one big artifact, two big buttons --------------
    private void DrawHomeSenior(Graphics g, int contentY)
    {
        // Senior layout reserves room on the right for the transcription panel
        // (drawn by DrawAdaptiveOverlays) and at the bottom for the caption strip
        // and the two giant buttons.
        int W = this.ClientSize.Width;
        int H = this.ClientSize.Height;
        int padX = 50;
        int rightReserve = (activeProfile != null && activeProfile.ShowTranscription) ? 360 : 20;
        int innerLeft = padX;
        int innerRight = W - rightReserve;
        int innerW = innerRight - innerLeft;

        // Bottom-strip caption (DrawAdaptiveOverlays) eats 50px; buttons need 72px
        // tall plus 24px gap above caption and 18px gap above card.
        int bottomStripH = 50;
        int btnH = 72;
        int btnY = H - bottomStripH - 18 - btnH;            // top of buttons
        int cardBottomMax = btnY - 18;                       // 18px gap to buttons

        // Welcome line, big — capped so it doesn't push the card down.
        Font helloFont = new Font("Segoe UI", 26f, FontStyle.Bold);
        Font subFont   = new Font("Segoe UI", 16f, FontStyle.Regular);
        g.DrawString("Welcome, " + uname, helloFont, fntBrush, padX, contentY);
        g.DrawString("Today's featured artifact", subFont, textLightBrush, padX, contentY + 42);

        ArtifactRecord featured = (artifacts != null && artifacts.Count > 0) ? artifacts[0] : null;

        // Hero card — sized to fit the available vertical band.
        int cardY = contentY + 80;
        int cardH = Math.Max(220, cardBottomMax - cardY);
        Rectangle card = new Rectangle(innerLeft, cardY, innerW, cardH);
        FillRoundedRect(g, cardBsh_dynamic, card, 24);
        DrawRoundedRect(g, borderPen, card, 24);

        // Image takes ~40% width with a fixed aspect, vertically centered.
        int imgPad = 22;
        int imgW = (int)(card.Width * 0.42f);
        int imgH = card.Height - imgPad * 2;
        Rectangle imgR = new Rectangle(card.X + imgPad, card.Y + imgPad, imgW, imgH);
        DrawArtifactImageRounded(g, featured, imgR, 18, currentTheme.avatarBackground);

        // Text column to the right of the image — measured with FormatString to
        // avoid overflow.
        int txtX = imgR.Right + 26;
        int txtW = card.Right - txtX - 22;
        Font nameFont = new Font("Segoe UI", 26f, FontStyle.Bold);
        Font eraFont  = new Font("Segoe UI", 16f, FontStyle.Regular);
        Font descFont = new Font("Segoe UI", 15f, FontStyle.Regular);

        string nameText = featured != null ? featured.name : "—";
        string eraText  = featured != null ? (featured.era ?? "") : "";
        SizeF nSize = g.MeasureString(nameText, nameFont, txtW);
        SizeF eSize = g.MeasureString(eraText,  eraFont,  txtW);

        int ty = card.Y + imgPad;
        g.DrawString(nameText, nameFont, fntBrush,
            new RectangleF(txtX, ty, txtW, nSize.Height + 6));
        ty += (int)nSize.Height + 10;
        g.DrawString(eraText, eraFont, textLightBrush,
            new RectangleF(txtX, ty, txtW, eSize.Height + 4));
        ty += (int)eSize.Height + 14;

        if (featured != null && !string.IsNullOrEmpty(featured.description))
        {
            int descMaxH = (card.Bottom - imgPad) - ty;
            if (descMaxH > 30)
            {
                StringFormat sf = new StringFormat { Trimming = StringTrimming.EllipsisWord };
                g.DrawString(featured.description, descFont, fntBrush,
                    new RectangleF(txtX, ty, txtW, descMaxH), sf);
            }
        }

        // Two giant buttons under the card — sit ABOVE the bottom strip.
        int btnGap = 24;
        int btnW = (innerW - btnGap) / 2;
        Rectangle btnHear = new Rectangle(innerLeft, btnY, btnW, btnH);
        Rectangle btnNext = new Rectangle(innerLeft + btnW + btnGap, btnY, btnW, btnH);

        FillRoundedRect(g, accentBrush.Color, btnHear, 22);
        DrawStringCentered(g, "▶  Hear About This",
            new Font("Segoe UI", 18f, FontStyle.Bold), Brushes.White, btnHear);
        if (featured != null)
            artifactClickTargets.Add(new ArtifactClickTarget { Bounds = btnHear, ArtifactId = featured.id });

        FillRoundedRect(g, blbBrush, btnNext, 22);
        DrawRoundedRect(g, new Pen(accentBrush.Color, 2), btnNext, 22);
        DrawStringCentered(g, "Next Artifact  →",
            new Font("Segoe UI", 18f, FontStyle.Bold), accentBrush, btnNext);
        pageClickTargets.Add(new PageClickTarget { Bounds = btnNext, PageIndex = 2 });
    }

    // =====================================================================
    //                      DETAILS PAGE — per-age layouts
    // =====================================================================

    private void DrawDetailsChild(Graphics g, int contentY, ArtifactRecord a)
    {
        int W = this.ClientSize.Width;
        int H = this.ClientSize.Height;
        int padX = 40;
        int innerW = W - padX * 2;

        // Friendly title bar
        Rectangle hero = new Rectangle(padX, contentY, innerW, 70);
        FillRoundedGradient(g, hero,
            Color.FromArgb(135, 201, 255), Color.FromArgb(255, 153, 199), 18);
        g.DrawString(a.name ?? "Cool Find!",
            new Font("Segoe UI", 22f, FontStyle.Bold), Brushes.White, hero.X + 24, hero.Y + 18);

        // Centered huge image
        int imgSize = Math.Min(420, H - hero.Bottom - 220);
        Rectangle imgR = new Rectangle(padX + (innerW - imgSize) / 2, hero.Bottom + 20, imgSize, imgSize);
        FillRoundedRect(g, Color.White, imgR, 24);
        DrawRoundedRect(g, new Pen(Color.FromArgb(255, 219, 102), 6), imgR, 24);
        DrawArtifactImageRounded(g, a, imgR, 22, Color.White);

        // Short fact line
        string fact = (a.era ?? "Long ago") + "  •  " + (a.origin ?? "Egypt");
        Font factFont = new Font("Segoe UI", 16f, FontStyle.Bold);
        SizeF fs = g.MeasureString(fact, factFont);
        g.DrawString(fact, factFont, fntBrush,
            padX + (innerW - fs.Width) / 2, imgR.Bottom + 14);

        // Two big tappable buttons
        bool isFav = IsFavoriteArtifact(a.id);
        int btnH = 80;
        int btnW = (innerW - 20) / 2;
        int btnY = H - btnH - 90; // leave room for nav hint
        Rectangle btnListen = new Rectangle(padX, btnY, btnW, btnH);
        Rectangle btnFav    = new Rectangle(padX + btnW + 20, btnY, btnW, btnH);

        FillRoundedRect(g, Color.FromArgb(160, 220, 130), btnListen, 28);
        DrawStringCentered(g, audioMuted ? "🔇  Listen" : "🔊  Listen",
            new Font("Segoe UI", 22f, FontStyle.Bold), Brushes.White, btnListen);
        audioToggleButtonRect = btnListen;

        FillRoundedRect(g,
            isFav ? Color.FromArgb(255, 105, 145) : Color.FromArgb(255, 188, 102),
            btnFav, 28);
        DrawStringCentered(g, isFav ? "❤  Saved!" : "♡  Save",
            new Font("Segoe UI", 22f, FontStyle.Bold), Brushes.White, btnFav);
        favoriteToggleButtonRect = btnFav;
    }

    private void DrawDetailsSenior(Graphics g, int contentY, ArtifactRecord a)
    {
        int W = this.ClientSize.Width;
        int H = this.ClientSize.Height;
        int padX = 50;
        int rightReserve = (activeProfile != null && activeProfile.ShowTranscription) ? 360 : 20;
        int innerLeft = padX;
        int innerRight = W - rightReserve;
        int innerW = innerRight - innerLeft;

        // Big title
        g.DrawString(a.name ?? "Artifact",
            new Font("Segoe UI", 28f, FontStyle.Bold), fntBrush, innerLeft, contentY);
        g.DrawString((a.era ?? "") + (string.IsNullOrEmpty(a.origin) ? "" : "  •  " + a.origin),
            new Font("Segoe UI", 16f, FontStyle.Regular), textLightBrush, innerLeft, contentY + 42);

        // Reserve room for caption strip + 3 big buttons
        int bottomReserve = 50; // caption strip
        int btnH = 70;
        int btnGap = 16;
        int btnY = H - bottomReserve - 18 - btnH;
        int cardTop = contentY + 86;
        int cardBottom = btnY - 18;
        Rectangle card = new Rectangle(innerLeft, cardTop, innerW, cardBottom - cardTop);
        FillRoundedRect(g, cardBsh_dynamic, card, 22);
        DrawRoundedRect(g, borderPen, card, 22);

        // Image left half, description right half
        int imgPad = 22;
        int imgW = (int)(card.Width * 0.45f);
        Rectangle imgR = new Rectangle(card.X + imgPad, card.Y + imgPad, imgW, card.Height - imgPad * 2);
        DrawArtifactImageRounded(g, a, imgR, 18, currentTheme.avatarBackground);

        int tx = imgR.Right + 24;
        int tw = card.Right - tx - 22;
        Font descFont = new Font("Segoe UI", 17f);
        StringFormat sf = new StringFormat { Trimming = StringTrimming.EllipsisWord };
        if (!string.IsNullOrEmpty(a.description))
            g.DrawString(a.description, descFont, fntBrush,
                new RectangleF(tx, imgR.Y, tw, imgR.Height), sf);

        // 3 huge buttons: Listen, Favourite, Next
        int btnCount = 3;
        int btnW = (innerW - btnGap * (btnCount - 1)) / btnCount;
        Rectangle btnListen = new Rectangle(innerLeft, btnY, btnW, btnH);
        Rectangle btnFav    = new Rectangle(innerLeft + (btnW + btnGap), btnY, btnW, btnH);
        Rectangle btnNext   = new Rectangle(innerLeft + (btnW + btnGap) * 2, btnY, btnW, btnH);

        FillRoundedRect(g, accentBrush.Color, btnListen, 22);
        DrawStringCentered(g, audioMuted ? "▶  Unmute" : "▶  Listen",
            new Font("Segoe UI", 18f, FontStyle.Bold), Brushes.White, btnListen);
        audioToggleButtonRect = btnListen;

        bool isFav = IsFavoriteArtifact(a.id);
        FillRoundedRect(g, blbBrush, btnFav, 22);
        DrawRoundedRect(g, new Pen(accentBrush.Color, 2), btnFav, 22);
        DrawStringCentered(g, isFav ? "♥  Saved" : "♡  Save",
            new Font("Segoe UI", 18f, FontStyle.Bold), accentBrush, btnFav);
        favoriteToggleButtonRect = btnFav;

        FillRoundedRect(g, blbBrush, btnNext, 22);
        DrawRoundedRect(g, new Pen(accentBrush.Color, 2), btnNext, 22);
        DrawStringCentered(g, "Next  →",
            new Font("Segoe UI", 18f, FontStyle.Bold), accentBrush, btnNext);
        pageClickTargets.Add(new PageClickTarget { Bounds = btnNext, PageIndex = 2 });
    }

    // =====================================================================
    //                     EXPLORE PAGE — per-age layouts
    // =====================================================================

    private void DrawExploreChild(Graphics g, int contentY)
    {
        int W = this.ClientSize.Width;
        int padX = 40;
        int innerW = W - padX * 2;

        Rectangle hero = new Rectangle(padX, contentY, innerW, 90);
        FillRoundedGradient(g, hero,
            Color.FromArgb(160, 220, 130), Color.FromArgb(98, 199, 246), 22);
        g.DrawString("Where to next? 🎲",
            new Font("Segoe UI", 26f, FontStyle.Bold), Brushes.White, hero.X + 28, hero.Y + 24);

        // Three big zone tiles
        var zones = new[] {
            new { Title="Ancient Egypt 🐪",       Col = Color.FromArgb(255, 188, 102) },
            new { Title="Royal Collection 👑",    Col = Color.FromArgb(196, 174, 240) },
            new { Title="Sculpture Hall 🗿",      Col = Color.FromArgb(135, 201, 255) }
        };
        int top = hero.Bottom + 20;
        int tileH = (this.ClientSize.Height - top - 90) / 3;
        for (int i = 0; i < zones.Length; i++)
        {
            Rectangle r = new Rectangle(padX, top + i * (tileH + 12), innerW, tileH);
            FillRoundedRect(g, zones[i].Col, r, 24);
            g.DrawString(zones[i].Title,
                new Font("Segoe UI", 24f, FontStyle.Bold), Brushes.White,
                r.X + 30, r.Y + (r.Height - 36) / 2);
        }
    }

    private void DrawExploreSenior(Graphics g, int contentY)
    {
        int W = this.ClientSize.Width;
        int H = this.ClientSize.Height;
        int padX = 50;
        int rightReserve = (activeProfile != null && activeProfile.ShowTranscription) ? 360 : 20;
        int innerLeft = padX;
        int innerW = W - rightReserve - innerLeft;
        int bottomReserve = 80;

        g.DrawString("Where to explore",
            new Font("Segoe UI", 26f, FontStyle.Bold), fntBrush, innerLeft, contentY);
        g.DrawString("Tap a stop to learn what is there.",
            new Font("Segoe UI", 16f), textLightBrush, innerLeft, contentY + 40);

        var stops = new[] { "Ancient Egypt", "Royal Collection", "Sculpture Hall" };
        int top = contentY + 84;
        int rowH = (H - top - bottomReserve - 24) / stops.Length;
        for (int i = 0; i < stops.Length; i++)
        {
            Rectangle r = new Rectangle(innerLeft, top + i * (rowH + 12), innerW, rowH);
            FillRoundedRect(g, cardBsh_dynamic, r, 20);
            DrawRoundedRect(g, borderPen, r, 20);

            // Number circle
            int dot = 56;
            Rectangle dotR = new Rectangle(r.X + 28, r.Y + (r.Height - dot) / 2, dot, dot);
            FillRoundedRect(g, accentBrush.Color, dotR, dot / 2);
            DrawStringCentered(g, (i + 1).ToString(),
                new Font("Segoe UI", 24f, FontStyle.Bold), Brushes.White, dotR);

            g.DrawString(stops[i],
                new Font("Segoe UI", 24f, FontStyle.Bold), fntBrush,
                dotR.Right + 28, r.Y + (r.Height - 36) / 2);
        }
    }

    // =====================================================================
    //                    FAVOURITES PAGE — per-age layouts
    // =====================================================================

    private void DrawFavouritesChild(Graphics g, int contentY)
    {
        int W = this.ClientSize.Width;
        int padX = 40;
        int innerW = W - padX * 2;

        // Hero with stars
        Rectangle hero = new Rectangle(padX, contentY, innerW, 110);
        FillRoundedGradient(g, hero,
            Color.FromArgb(255, 219, 86), Color.FromArgb(255, 165, 102), 22);
        g.DrawString("My Star Picks ⭐",
            new Font("Segoe UI", 28f, FontStyle.Bold), Brushes.White, hero.X + 28, hero.Y + 32);

        List<ArtifactRecord> favs = new List<ArtifactRecord>();
        if (currentUser != null && currentUser.favorites != null)
        {
            foreach (int id in currentUser.favorites)
            {
                var a = GetArtifactById(id); if (a != null) favs.Add(a);
            }
        }

        if (favs.Count == 0)
        {
            // Friendly empty state
            Rectangle r = new Rectangle(padX, hero.Bottom + 30, innerW, 250);
            FillRoundedRect(g, blbBrush, r, 22);
            g.DrawString("⭐",
                new Font("Segoe UI Emoji", 64f), Brushes.White, r.X + (r.Width / 2) - 32, r.Y + 30);
            DrawStringCentered(g, "No stars yet — open something you love and tap the heart!",
                new Font("Segoe UI", 16f, FontStyle.Bold), accentBrush, r);
            return;
        }

        int top = hero.Bottom + 20;
        int gap = 18;
        int cols = 3;
        int cardW = (innerW - gap * (cols - 1)) / cols;
        int cardH = 230;
        int n = Math.Min(favs.Count, 6);
        Color[] palette = {
            Color.FromArgb(255, 188, 102), Color.FromArgb(255, 153, 199),
            Color.FromArgb(160, 220, 130), Color.FromArgb(135, 201, 255),
            Color.FromArgb(255, 219, 102), Color.FromArgb(196, 174, 240)
        };
        for (int i = 0; i < n; i++)
        {
            ArtifactRecord a = favs[i];
            int col = i % cols, row = i / cols;
            Rectangle r = new Rectangle(padX + col * (cardW + gap),
                top + row * (cardH + gap), cardW, cardH);
            FillRoundedRect(g, palette[i % palette.Length], r, 24);

            Rectangle imgR = new Rectangle(r.X + 12, r.Y + 12, r.Width - 24, r.Height - 70);
            FillRoundedRect(g, Color.White, imgR, 16);
            DrawArtifactImageRounded(g, a, imgR, 16, Color.White);

            // Star in top-right
            g.DrawString("⭐", new Font("Segoe UI Emoji", 22f), Brushes.White, r.Right - 44, r.Y + 12);

            string label = a.name ?? ("Artifact " + a.id);
            if (label.Length > 22) label = label.Substring(0, 21) + "…";
            g.DrawString(label,
                new Font("Segoe UI", 13f, FontStyle.Bold), Brushes.White,
                r.X + 16, r.Bottom - 42);

            artifactClickTargets.Add(new ArtifactClickTarget { Bounds = r, ArtifactId = a.id });
        }
    }

    private void DrawFavouritesSenior(Graphics g, int contentY)
    {
        int W = this.ClientSize.Width;
        int H = this.ClientSize.Height;
        int padX = 50;
        int rightReserve = (activeProfile != null && activeProfile.ShowTranscription) ? 360 : 20;
        int innerLeft = padX;
        int innerW = W - rightReserve - innerLeft;
        int bottomReserve = 80;

        g.DrawString("My Favourites",
            new Font("Segoe UI", 26f, FontStyle.Bold), fntBrush, innerLeft, contentY);

        List<ArtifactRecord> favs = new List<ArtifactRecord>();
        if (currentUser != null && currentUser.favorites != null)
        {
            foreach (int id in currentUser.favorites)
            {
                var a = GetArtifactById(id); if (a != null) favs.Add(a);
            }
        }

        int top = contentY + 56;
        if (favs.Count == 0)
        {
            Rectangle r = new Rectangle(innerLeft, top, innerW, 180);
            FillRoundedRect(g, cardBsh_dynamic, r, 20);
            DrawRoundedRect(g, borderPen, r, 20);
            DrawStringCentered(g,
                "You have not saved any favourites yet.",
                new Font("Segoe UI", 20f, FontStyle.Regular), textLightBrush, r);
            return;
        }

        int rowH = 120;
        int rowGap = 14;
        int maxRows = Math.Max(1, (H - top - bottomReserve) / (rowH + rowGap));
        int n = Math.Min(favs.Count, maxRows);
        for (int i = 0; i < n; i++)
        {
            ArtifactRecord a = favs[i];
            Rectangle r = new Rectangle(innerLeft, top + i * (rowH + rowGap), innerW, rowH);
            FillRoundedRect(g, cardBsh_dynamic, r, 18);
            DrawRoundedRect(g, borderPen, r, 18);

            Rectangle imgR = new Rectangle(r.X + 12, r.Y + 12, r.Height - 24, r.Height - 24);
            DrawArtifactImageRounded(g, a, imgR, 12, currentTheme.avatarBackground);

            int tx = imgR.Right + 22;
            g.DrawString(a.name ?? "Artifact",
                new Font("Segoe UI", 20f, FontStyle.Bold), fntBrush, tx, r.Y + 20);
            g.DrawString(a.era ?? "",
                new Font("Segoe UI", 14f, FontStyle.Regular), textLightBrush, tx, r.Y + 56);

            Rectangle openBtn = new Rectangle(r.Right - 200, r.Y + 30, 170, r.Height - 60);
            FillRoundedRect(g, accentBrush.Color, openBtn, openBtn.Height / 2);
            DrawStringCentered(g, "Open  →",
                new Font("Segoe UI", 14f, FontStyle.Bold), Brushes.White, openBtn);

            artifactClickTargets.Add(new ArtifactClickTarget { Bounds = r, ArtifactId = a.id });
        }
    }

    // =====================================================================
    //                      ARTIFACTS PAGE — per-age layouts
    // =====================================================================

    private void DrawArtifactsChild(Graphics g, int contentY)
    {
        int padX = 40;
        int W = this.ClientSize.Width;
        int innerW = W - padX * 2;

        // Bright header strip
        Rectangle hero = new Rectangle(padX, contentY, innerW, 80);
        FillRoundedGradient(g, hero,
            Color.FromArgb(98, 199, 246), Color.FromArgb(135, 206, 250), 18);
        g.DrawString("Look at all the things! 🏺",
            new Font("Segoe UI", 24f, FontStyle.Bold), Brushes.White, hero.X + 24, hero.Y + 24);

        // 2×3 grid of bright square cards (only 6 — kids don't want to scroll)
        int top = hero.Bottom + 20;
        int gap = 18;
        int cols = 3;
        int rows = 2;
        int cardW = (innerW - gap * (cols - 1)) / cols;
        int cardH = (this.ClientSize.Height - top - 70 - gap * (rows - 1)) / rows;
        Color[] palette = {
            Color.FromArgb(255, 188, 102),
            Color.FromArgb(255, 153, 199),
            Color.FromArgb(160, 220, 130),
            Color.FromArgb(255, 219, 102),
            Color.FromArgb(135, 201, 255),
            Color.FromArgb(196, 174, 240)
        };
        int n = Math.Min(artifacts != null ? artifacts.Count : 0, cols * rows);
        for (int i = 0; i < n; i++)
        {
            ArtifactRecord a = artifacts[i];
            int col = i % cols, row = i / cols;
            Rectangle r = new Rectangle(padX + col * (cardW + gap), top + row * (cardH + gap), cardW, cardH);
            FillRoundedRect(g, palette[i % palette.Length], r, 24);

            // White inner panel for image
            Rectangle imgR = new Rectangle(r.X + 12, r.Y + 12, r.Width - 24, r.Height - 70);
            FillRoundedRect(g, Color.White, imgR, 16);
            DrawArtifactImageRounded(g, a, imgR, 16, Color.White);

            // Bottom label
            string label = a.name ?? ("Artifact " + a.id);
            if (label.Length > 22) label = label.Substring(0, 21) + "…";
            g.DrawString(label,
                new Font("Segoe UI", 13f, FontStyle.Bold), Brushes.White,
                r.X + 16, r.Bottom - 42);

            artifactClickTargets.Add(new ArtifactClickTarget { Bounds = r, ArtifactId = a.id });
        }
    }

    private void DrawArtifactsSenior(Graphics g, int contentY)
    {
        int W = this.ClientSize.Width;
        int H = this.ClientSize.Height;
        int padX = 50;
        int rightReserve = (activeProfile != null && activeProfile.ShowTranscription) ? 360 : 20;
        int innerLeft = padX;
        int innerRight = W - rightReserve;
        int innerW = innerRight - innerLeft;
        int bottomReserve = 80;

        g.DrawString("All Artifacts",
            new Font("Segoe UI", 26f, FontStyle.Bold), fntBrush, innerLeft, contentY);

        // Vertical list of big rows (image left, big name + era right)
        int listTop = contentY + 56;
        int rowH = 130;
        int rowGap = 14;
        int maxRows = Math.Max(1, (H - listTop - bottomReserve) / (rowH + rowGap));
        int n = Math.Min(artifacts != null ? artifacts.Count : 0, maxRows);
        for (int i = 0; i < n; i++)
        {
            ArtifactRecord a = artifacts[i];
            Rectangle r = new Rectangle(innerLeft, listTop + i * (rowH + rowGap), innerW, rowH);
            FillRoundedRect(g, cardBsh_dynamic, r, 18);
            DrawRoundedRect(g, borderPen, r, 18);

            Rectangle imgR = new Rectangle(r.X + 14, r.Y + 14, r.Height - 28, r.Height - 28);
            DrawArtifactImageRounded(g, a, imgR, 12, currentTheme.avatarBackground);

            int tx = imgR.Right + 24;
            g.DrawString(a.name ?? "Artifact",
                new Font("Segoe UI", 22f, FontStyle.Bold), fntBrush, tx, r.Y + 22);
            g.DrawString(a.era ?? "",
                new Font("Segoe UI", 16f, FontStyle.Regular), textLightBrush, tx, r.Y + 60);

            // Big right-aligned arrow as the call to action
            Rectangle openBtn = new Rectangle(r.Right - 200, r.Y + 30, 170, r.Height - 60);
            FillRoundedRect(g, accentBrush.Color, openBtn, openBtn.Height / 2);
            DrawStringCentered(g, "Open  →",
                new Font("Segoe UI", 16f, FontStyle.Bold), Brushes.White, openBtn);

            artifactClickTargets.Add(new ArtifactClickTarget { Bounds = r, ArtifactId = a.id });
        }
    }

    // PROFILE PAGE — per-age layouts

    private void DrawProfileChild(Graphics g, int contentY)
    {
        int padX = 40;
        int W = this.ClientSize.Width;
        int innerW = W - padX * 2;

        // Hero greeting with avatar inside it
        Rectangle hero = new Rectangle(padX, contentY, innerW, 200);
        FillRoundedGradient(g, hero,
            Color.FromArgb(255, 153, 199),
            Color.FromArgb(135, 201, 255),
            24);

        int av = 140;
        Rectangle avR = new Rectangle(hero.X + 30, hero.Y + (hero.Height - av) / 2, av, av);
        FillRoundedRect(g, Color.White, avR, av / 2);
        DrawRoundedRect(g, new Pen(Color.White, 5), avR, av / 2);
        if (upic != null)
        {
            Region prev = g.Clip;
            using (GraphicsPath circle = new GraphicsPath())
            {
                circle.AddEllipse(avR);
                g.SetClip(circle);
                g.DrawImage(upic, avR);
            }
            g.Clip = prev;
        }

        g.DrawString("Hi " + uname + "! 👋",
            new Font("Segoe UI", 30f, FontStyle.Bold), Brushes.White, avR.Right + 26, hero.Y + 36);
        g.DrawString("This is YOU at the Museum",
            new Font("Segoe UI", 14f, FontStyle.Bold),
            new SolidBrush(Color.FromArgb(230, 255, 255, 255)),
            avR.Right + 28, hero.Y + 90);
        g.DrawString("Tap a tile to play!",
            new Font("Segoe UI Emoji", 14f),
            new SolidBrush(Color.FromArgb(220, 255, 255, 255)),
            avR.Right + 28, hero.Y + 130);

        // 3 big emoji tiles (Age, Favourites, Theme)
        int tileTop = hero.Bottom + 24;
        int gap = 20;
        int tileW = (innerW - gap * 2) / 3;
        int tileH = this.ClientSize.Height - tileTop - 80;
        if (tileH > 280) tileH = 280;
        int favCount = currentUser != null && currentUser.favorites != null ? currentUser.favorites.Count : 0;
        string ageVal = currentUser != null ? (currentUser.age ?? "?") : "?";
        var tiles = new[] {
            new { Top="My Age",       Big = ageVal,           Emoji = "🎂", Col = Color.FromArgb(255, 188, 102) },
            new { Top="My Favourites",Big = favCount.ToString(), Emoji = "⭐", Col = Color.FromArgb(255, 219, 102) },
            new { Top="My Theme",     Big = currentThemeMode.Substring(0,1).ToUpper()+currentThemeMode.Substring(1), Emoji = "🎨", Col = Color.FromArgb(160, 220, 130) },
        };
        Font emFont    = new Font("Segoe UI Emoji", 52f);
        Font tileTop_F = new Font("Segoe UI", 12f, FontStyle.Bold);
        Font tileBigF  = new Font("Segoe UI", 30f, FontStyle.Bold);

        for (int i = 0; i < tiles.Length; i++)
        {
            Rectangle r = new Rectangle(padX + i * (tileW + gap), tileTop, tileW, tileH);
            FillRoundedRect(g, tiles[i].Col, r, 28);

            SizeF em = g.MeasureString(tiles[i].Emoji, emFont);
            g.DrawString(tiles[i].Emoji, emFont, Brushes.White,
                r.X + (r.Width - em.Width) / 2, r.Y + 24);

            g.DrawString(tiles[i].Top, tileTop_F, Brushes.White,
                r.X + 24, r.Y + r.Height - 78);
            g.DrawString(tiles[i].Big, tileBigF, Brushes.White,
                r.X + 24, r.Y + r.Height - 60);
        }
    }

    private void DrawProfileDetailed(Graphics g, int contentY)
    {
        // Teen + Adult shared design: rounded card with avatar, age/gender,
        // tag chips, and a row of stat KPIs.
        int padX = 50;
        int innerW = this.ClientSize.Width - padX * 2;

        g.DrawString("Your Profile",
            new Font("Segoe UI", 26f, FontStyle.Bold), fntBrush, padX, contentY);

        int cardY = contentY + 56;
        int cardH = 400;
        Rectangle card = new Rectangle(padX, cardY, innerW, cardH);
        FillRoundedRect(g, cardBsh_dynamic, card, 22);
        DrawRoundedRect(g, borderPen, card, 22);

        // Accent stripe at top
        FillRoundedRect(g, accentBrush.Color,
            new Rectangle(card.X, card.Y, card.Width, 8), 8);

        int av = 180;
        Rectangle avR = new Rectangle(card.X + 36, card.Y + 36, av, av);
        FillRoundedRect(g, avatarBrush, avR, av / 2);
        DrawRoundedRect(g, new Pen(currentTheme.border, 2), avR, av / 2);
        if (upic != null)
        {
            Region prev = g.Clip;
            using (GraphicsPath circle = new GraphicsPath())
            {
                circle.AddEllipse(avR);
                g.SetClip(circle);
                g.DrawImage(upic, avR);
            }
            g.Clip = prev;
        }

        int infoX = avR.Right + 36;
        int ly = card.Y + 38;
        g.DrawString(uname,
            new Font("Segoe UI", 24f, FontStyle.Bold), fntBrush, infoX, ly);
        ly += 46;

        string ageGenderLine = currentUser != null
            ? ((currentUser.age ?? "—") + " yrs  •  " + (currentUser.gender ?? "—"))
            : "No detailed record loaded";
        g.DrawString(ageGenderLine, new Font("Segoe UI", 13f), textLightBrush, infoX, ly);
        ly += 36;

        // Pill chips
        Font chipFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        string themeChip = "Theme: " + currentThemeMode.Substring(0,1).ToUpper() + currentThemeMode.Substring(1);
        string modeChip  = "Mode: "  + (activeProfile != null ? activeProfile.Label : "Adult");
        SizeF s1 = g.MeasureString(themeChip, chipFont);
        SizeF s2 = g.MeasureString(modeChip, chipFont);
        Rectangle chip1 = new Rectangle(infoX, ly, (int)s1.Width + 22, 28);
        Rectangle chip2 = new Rectangle(chip1.Right + 10, ly, (int)s2.Width + 22, 28);
        FillRoundedRect(g, blbBrush, chip1, 14);
        FillRoundedRect(g, blbBrush, chip2, 14);
        DrawStringCentered(g, themeChip, chipFont, accentBrush, chip1);
        DrawStringCentered(g, modeChip,  chipFont, accentBrush, chip2);
        ly += 50;

        // KPI tiles
        int kpiW = 180, kpiH = 95, kpiGap = 16;
        string[] kLabels = { "Bluetooth", "Favourites", "Theme" };
        string[] kVals = {
            "Matched",
            currentUser != null && currentUser.favorites != null ? currentUser.favorites.Count.ToString() : "0",
            currentThemeMode.Substring(0,1).ToUpper()+currentThemeMode.Substring(1) + " mode"
        };
        for (int i = 0; i < 3; i++)
        {
            Rectangle k = new Rectangle(infoX + i * (kpiW + kpiGap), ly, kpiW, kpiH);
            FillRoundedRect(g, blbBrush, k, 14);
            g.DrawString(kLabels[i], new Font("Segoe UI", 10f, FontStyle.Bold), textLightBrush, k.X + 16, k.Y + 14);
            g.DrawString(kVals[i],  new Font("Segoe UI", 14f, FontStyle.Bold), fntBrush, k.X + 16, k.Y + 40);
        }
    }

    private void DrawProfileSenior(Graphics g, int contentY)
    {
        // Centered, single-column, very large. Account for transcription panel
        // on the right and the bottom caption strip below.
        int W = this.ClientSize.Width;
        int H = this.ClientSize.Height;
        int rightReserve = (activeProfile != null && activeProfile.ShowTranscription) ? 360 : 20;
        int innerLeft = 60;
        int innerRight = W - rightReserve;
        int innerW = innerRight - innerLeft;
        int bottomReserve = 80; // caption strip + margin

        Rectangle card = new Rectangle(innerLeft, contentY,
            innerW, H - contentY - bottomReserve);
        FillRoundedRect(g, cardBsh_dynamic, card, 22);
        DrawRoundedRect(g, borderPen, card, 22);

        // Avatar large, centered horizontally
        int av = 200;
        Rectangle avR = new Rectangle(card.X + (card.Width - av) / 2, card.Y + 30, av, av);
        FillRoundedRect(g, avatarBrush, avR, av / 2);
        DrawRoundedRect(g, new Pen(currentTheme.border, 3), avR, av / 2);
        if (upic != null)
        {
            Region prev = g.Clip;
            using (GraphicsPath circle = new GraphicsPath())
            {
                circle.AddEllipse(avR);
                g.SetClip(circle);
                g.DrawImage(upic, avR);
            }
            g.Clip = prev;
        }

        Font nameFont = new Font("Segoe UI", 30f, FontStyle.Bold);
        SizeF nSize = g.MeasureString(uname, nameFont);
        g.DrawString(uname, nameFont, fntBrush,
            card.X + (card.Width - nSize.Width) / 2, avR.Bottom + 20);

        // Big readable info rows full width, generous line height
        Font keyF = new Font("Segoe UI", 18f, FontStyle.Bold);
        Font valF = new Font("Segoe UI", 18f, FontStyle.Regular);
        int rowsY = avR.Bottom + 90;
        int rowH = 56;
        string[][] rows = currentUser != null
            ? new string[][] {
                new[] { "Age",        currentUser.age ?? "—" },
                new[] { "Gender",     currentUser.gender ?? "—" },
                new[] { "Favourites", (currentUser.favorites != null ? currentUser.favorites.Count : 0).ToString() },
                new[] { "Theme",      currentThemeMode.Substring(0,1).ToUpper()+currentThemeMode.Substring(1) },
              }
            : new string[][] { new[] { "Status", "No user record yet" } };

        for (int i = 0; i < rows.Length && rowsY + rowH < card.Bottom - 20; i++)
        {
            int rowX = card.X + 60;
            int rowW = card.Width - 120;
            Rectangle row = new Rectangle(rowX, rowsY + i * rowH, rowW, rowH - 8);
            if (i % 2 == 0)
                FillRoundedRect(g, blbBrush, row, 12);

            g.DrawString(rows[i][0], keyF, accentBrush, row.X + 24, row.Y + 12);
            g.DrawString(rows[i][1], valF, fntBrush,    row.X + 280, row.Y + 12);
        }
    }

    //Drawing primitives (rounded corners, gradients)
    private static GraphicsPath BuildRoundedRectPath(Rectangle r, int radius)
    {
        int d = Math.Max(0, radius * 2);
        GraphicsPath path = new GraphicsPath();
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void FillRoundedRect(Graphics g, Brush brush, Rectangle r, int radius)
    {
        using (GraphicsPath p = BuildRoundedRectPath(r, radius)) g.FillPath(brush, p);
    }

    private void FillRoundedRect(Graphics g, Color color, Rectangle r, int radius)
    {
        using (SolidBrush b = new SolidBrush(color))
        using (GraphicsPath p = BuildRoundedRectPath(r, radius)) g.FillPath(b, p);
    }

    private void DrawRoundedRect(Graphics g, Pen pen, Rectangle r, int radius)
    {
        using (GraphicsPath p = BuildRoundedRectPath(r, radius)) g.DrawPath(pen, p);
    }

    private void FillRoundedGradient(Graphics g, Rectangle r, Color from, Color to, int radius)
    {
        using (GraphicsPath p = BuildRoundedRectPath(r, radius))
        using (LinearGradientBrush b = new LinearGradientBrush(r, from, to, 45f))
        {
            g.FillPath(b, p);
        }
    }

    //center and draw a string inside a rectangle.
    private void DrawStringCentered(Graphics g, string text, Font font, Brush brush, Rectangle r)
    {
        SizeF sz = g.MeasureString(text, font);
        g.DrawString(text, font, brush,
            r.X + (r.Width  - sz.Width)  / 2,
            r.Y + (r.Height - sz.Height) / 2);
    }

    // Draw an artifact image fitted into a rectangle (rounded top), or a placeholder.
    private void DrawArtifactImageRounded(Graphics g, ArtifactRecord artifact, Rectangle rect, int radius, Color placeholderColor)
    {
        Region oldClip = g.Clip;
        try
        {
            using (GraphicsPath clip = BuildRoundedRectPath(rect, radius))
            {
                g.SetClip(clip);
                bool drew = false;
                if (artifact != null)
                {
                    string path = ResolveArtifactAssetPath(artifact.objPath);
                    if (File.Exists(path))
                    {
                        try
                        {
                            using (Image img = Image.FromFile(path))
                                g.DrawImage(img, rect);
                            drew = true;
                        }
                        catch { }
                    }
                }
                if (!drew)
                {
                    using (SolidBrush pb = new SolidBrush(placeholderColor))
                        g.FillRectangle(pb, rect);
                }
            }
        }
        finally { g.Clip = oldClip; }
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
      
            this.pictureBox1.Location = new System.Drawing.Point(190, 101);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(120, 120);
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;

            this.lblHello.AutoSize = true;
            this.lblHello.Font = new System.Drawing.Font("Arial", 22F);
            this.lblHello.ForeColor = System.Drawing.Color.Cornsilk;
            this.lblHello.Location = new System.Drawing.Point(149, 254);
            this.lblHello.Name = "lblHello";
            this.lblHello.Size = new System.Drawing.Size(219, 42);
            this.lblHello.TabIndex = 1;
            this.lblHello.Text = "Hello, Visitor";
            this.lblHello.Click += new System.EventHandler(this.lblHello_Click);
      
            this.lblStatus.AutoSize = true;
            this.lblStatus.Font = new System.Drawing.Font("Arial", 18F);
            this.lblStatus.ForeColor = System.Drawing.Color.Cornsilk;
            this.lblStatus.Location = new System.Drawing.Point(184, 319);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(140, 35);
            this.lblStatus.TabIndex = 2;
            this.lblStatus.Text = "Waiting...";
            this.lblStatus.Click += new System.EventHandler(this.label1_Click);
       
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

    // emotion reactive effects
    // Listens for Python "TRANS:Expression: <emotion>" lines. Spawns short
    // visual reactions on the GUI, throttled with confirmation + cooldowns so
    // the user is never carpet-bombed. Each age mode gets a different
    // intensity (Child = playful balloons; Senior = gentle text toast only).

    private abstract class Effect
    {
        public DateTime SpawnTime { get; private set; } = DateTime.Now;
        public double LifetimeSec { get; protected set; } = 4.0;
        public double Age => (DateTime.Now - SpawnTime).TotalSeconds;
        public bool IsAlive => Age < LifetimeSec;
        public double Progress => Math.Min(1.0, Age / LifetimeSec);
        public abstract void Draw(Graphics g, Rectangle bounds);

        protected static int Fade(double t, double fadeInEnd = 0.15, double fadeOutStart = 0.8)
        {
            if (t < fadeInEnd) return (int)(255 * (t / fadeInEnd));
            if (t > fadeOutStart) return (int)(255 * Math.Max(0, 1.0 - (t - fadeOutStart) / (1.0 - fadeOutStart)));
            return 255;
        }
    }

    // A multi-layer celebration: balloons rising with sway + tumbling confetti
    // pieces + twinkling sparkles. Staggered spawn, gradient-shaded balloon
    // bodies with curved strings, bright/varied confetti, and four-point sparkle
    // glints. Replaces the old single-particle balloon effect.
    private class BalloonBurstEffect : Effect
    {
        private struct Balloon
        {
            public float StartX, BaseRiseSpeed, SwayAmp, SwayPhase, SwayPeriod;
            public Color Color;
            public float Radius;
            public double Delay;
            public float Tilt;            // degrees, oscillating
            public float TiltSpeed;
        }

        private struct Confetto
        {
            public float StartX, StartY, FallSpeed, SwayAmp, SwayPeriod, SwayPhase;
            public Color Color;
            public float Size;            // long edge px
            public float Aspect;          // short/long ratio
            public float RotStart, RotSpeed; // degrees / s
            public double Delay;
            public int Shape;             // 0=rect, 1=triangle, 2=ribbon (squashed rect)
        }

        private struct Sparkle
        {
            public float X, Y, Size;
            public double Start, Duration; // absolute lifetimes within the effect
            public Color Color;
        }

        private readonly List<Balloon> balloons = new List<Balloon>();
        private readonly List<Confetto> confettis = new List<Confetto>();
        private readonly List<Sparkle> sparkles = new List<Sparkle>();

        public BalloonBurstEffect(int count, int radius, Color[] palette, Random rng)
        {
            LifetimeSec = 7.0;

            // ---- Balloons: rise from both bottom corners, varied sizes ----
            for (int i = 0; i < count; i++)
            {
                bool leftSide = (i % 2) == 0;
                float startX = leftSide
                    ? (float)(40 + rng.NextDouble() * 320)
                    : (float)(960 + rng.NextDouble() * 320);
                float r = radius + (float)(rng.NextDouble() * 14 - 7);
                balloons.Add(new Balloon
                {
                    StartX        = startX,
                    BaseRiseSpeed = (float)(95 + rng.NextDouble() * 55),  // px/s
                    SwayAmp       = (float)(14 + rng.NextDouble() * 18),
                    SwayPhase     = (float)(rng.NextDouble() * Math.PI * 2),
                    SwayPeriod    = (float)(2.4 + rng.NextDouble() * 1.6),
                    Color         = palette[rng.Next(palette.Length)],
                    Radius        = Math.Max(14, r),
                    Delay         = rng.NextDouble() * 1.2,
                    Tilt          = (float)((rng.NextDouble() - 0.5) * 6),
                    TiltSpeed     = (float)(0.7 + rng.NextDouble() * 0.8),
                });
            }

            // ---- Confetti: fall from top, tumble, scattered across width ----
            int confettiCount = (int)(count * 2.2);
            for (int i = 0; i < confettiCount; i++)
            {
                confettis.Add(new Confetto
                {
                    StartX     = (float)(rng.NextDouble() * 1200 + 30),
                    StartY     = (float)(-40 - rng.NextDouble() * 100),
                    FallSpeed  = (float)(70 + rng.NextDouble() * 90),
                    SwayAmp    = (float)(20 + rng.NextDouble() * 40),
                    SwayPeriod = (float)(1.8 + rng.NextDouble() * 1.6),
                    SwayPhase  = (float)(rng.NextDouble() * Math.PI * 2),
                    Color      = palette[rng.Next(palette.Length)],
                    Size       = (float)(9 + rng.NextDouble() * 9),
                    Aspect     = (float)(0.35 + rng.NextDouble() * 0.35),
                    RotStart   = (float)(rng.NextDouble() * 360),
                    RotSpeed   = (float)((rng.NextDouble() - 0.5) * 360),
                    Delay      = 0.35 + rng.NextDouble() * 2.0,
                    Shape      = rng.Next(3),
                });
            }

            // ---- Sparkles: tiny twinkling glints scattered in the upper half ----
            int sparkleCount = count * 2 + 12;
            for (int i = 0; i < sparkleCount; i++)
            {
                double start = rng.NextDouble() * (LifetimeSec - 1.0);
                sparkles.Add(new Sparkle
                {
                    X        = (float)(40 + rng.NextDouble() * 1200),
                    Y        = (float)(80  + rng.NextDouble() * 520),
                    Size     = (float)(5 + rng.NextDouble() * 7),
                    Start    = start,
                    Duration = 0.55 + rng.NextDouble() * 0.4,
                    Color    = Color.FromArgb(255,
                        220 + rng.Next(36), 220 + rng.Next(36), 180 + rng.Next(76)),
                });
            }
        }

        private static float EaseOutSine(float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return (float)Math.Sin(t * Math.PI / 2.0);
        }

        public override void Draw(Graphics g, Rectangle bounds)
        {
            // Higher-quality drawing for smooth ellipses + small text
            SmoothingMode prevSmooth = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                double now = Age;

                DrawConfetti(g, bounds, now);
                DrawSparkles(g, bounds, now);
                DrawBalloons(g, bounds, now);
            }
            finally { g.SmoothingMode = prevSmooth; }
        }

        private void DrawBalloons(Graphics g, Rectangle bounds, double now)
        {
            foreach (var b in balloons)
            {
                double t = now - b.Delay;
                if (t < 0) continue;
                double localProgress = Math.Min(1.0, t / (LifetimeSec - b.Delay));

                // Vertical rise with a soft ease-in (release feel) for first 0.6 s,
                // then constant terminal speed.
                float ease = t < 0.6 ? EaseOutSine((float)(t / 0.6)) : 1f;
                float distance = b.BaseRiseSpeed * (float)t * (0.6f + 0.4f * ease);
                float y = bounds.Height - 30 - distance;
                if (y < -b.Radius * 3) continue;

                // Horizontal sway around the spawn column
                float sway = (float)Math.Sin(t / b.SwayPeriod * Math.PI * 2 + b.SwayPhase) * b.SwayAmp;
                float x = b.StartX + sway;

                // Subtle scale "bob" — breathes 3% in and out, period ~1.6 s
                float scale = 1f + 0.03f * (float)Math.Sin(t * 4.2);

                // Tilt oscillates ±5°
                float tilt = b.Tilt + (float)Math.Sin(t * b.TiltSpeed) * 5f;

                int alpha = Fade(localProgress, 0.05, 0.78);
                if (alpha <= 0) continue;

                float radius = b.Radius * scale;

                // Save state, then translate+rotate so the balloon's "knot point"
                // sits at (x, y + radius * 1.05).
                GraphicsState state = g.Save();
                try
                {
                    g.TranslateTransform(x, y);
                    g.RotateTransform(tilt);

                    // Curved string drawn from knot down ~28-36 px
                    int stringAlpha = Math.Min(180, alpha);
                    using (var pen = new Pen(Color.FromArgb(stringAlpha, 60, 60, 60), 1.5f))
                    {
                        float kx = 0, ky = radius * 1.18f;
                        // Cubic Bezier for an S-curve string
                        PointF p0 = new PointF(kx, ky);
                        PointF p1 = new PointF(kx + 6, ky + 12);
                        PointF p2 = new PointF(kx - 6, ky + 22);
                        PointF p3 = new PointF(kx + 2, ky + 34);
                        g.DrawBezier(pen, p0, p1, p2, p3);
                    }

                    // Knot — small triangle below the balloon body
                    using (var knotBrush = new SolidBrush(Color.FromArgb(alpha,
                        DarkenColor(b.Color, 0.7f))))
                    {
                        PointF[] knot = {
                            new PointF(-3.5f, radius * 1.0f),
                            new PointF( 3.5f, radius * 1.0f),
                            new PointF( 0,    radius * 1.2f)
                        };
                        g.FillPolygon(knotBrush, knot);
                    }

                    // Balloon body — gradient (light top, full color bottom)
                    RectangleF bodyRect = new RectangleF(
                        -radius, -radius,
                        radius * 2, radius * 2.3f);

                    Color top = LightenColor(b.Color, 0.55f);
                    Color bottom = DarkenColor(b.Color, 0.92f);
                    using (var grad = new LinearGradientBrush(bodyRect,
                        Color.FromArgb(alpha, top),
                        Color.FromArgb(alpha, bottom),
                        90f))
                    {
                        g.FillEllipse(grad, bodyRect);
                    }

                    // Crisp outline
                    using (var outline = new Pen(Color.FromArgb((int)(alpha * 0.5f),
                        DarkenColor(b.Color, 0.55f)), 1.2f))
                    {
                        g.DrawEllipse(outline, bodyRect);
                    }

                    // Highlight gloss (upper-left ellipse, soft white)
                    using (var hb = new SolidBrush(Color.FromArgb(
                        (int)(alpha * 0.55), 255, 255, 255)))
                    {
                        g.FillEllipse(hb,
                            -radius * 0.55f, -radius * 0.78f,
                             radius * 0.55f,  radius * 0.45f);
                    }
                    using (var hb2 = new SolidBrush(Color.FromArgb(
                        (int)(alpha * 0.35), 255, 255, 255)))
                    {
                        g.FillEllipse(hb2,
                            -radius * 0.30f, -radius * 0.40f,
                             radius * 0.18f,  radius * 0.18f);
                    }
                }
                finally { g.Restore(state); }
            }
        }

        private void DrawConfetti(Graphics g, Rectangle bounds, double now)
        {
            foreach (var c in confettis)
            {
                double t = now - c.Delay;
                if (t < 0) continue;
                double maxLife = LifetimeSec - c.Delay;
                if (t > maxLife) continue;

                float y = c.StartY + c.FallSpeed * (float)t;
                if (y > bounds.Height + 20) continue;

                float sway = (float)Math.Sin(t / c.SwayPeriod * Math.PI * 2 + c.SwayPhase) * c.SwayAmp;
                float x = c.StartX + sway;
                float rot = c.RotStart + c.RotSpeed * (float)t;

                int alpha = Fade(Math.Min(1.0, t / maxLife), 0.08, 0.78);
                if (alpha <= 0) continue;

                GraphicsState s = g.Save();
                try
                {
                    g.TranslateTransform(x, y);
                    g.RotateTransform(rot);
                    using (var brush = new SolidBrush(Color.FromArgb(alpha, c.Color)))
                    {
                        if (c.Shape == 1)
                        {
                            // Triangle
                            float h = c.Size;
                            float w = c.Size * 0.85f;
                            PointF[] tri = {
                                new PointF(0,      -h/2),
                                new PointF(-w/2,    h/2),
                                new PointF( w/2,    h/2)
                            };
                            g.FillPolygon(brush, tri);
                        }
                        else
                        {
                            // Rectangle / ribbon
                            float h = c.Size;
                            float w = c.Size * c.Aspect;
                            g.FillRectangle(brush, -w / 2, -h / 2, w, h);
                            // Specular highlight band
                            using (var hb = new SolidBrush(Color.FromArgb(
                                (int)(alpha * 0.4), 255, 255, 255)))
                                g.FillRectangle(hb, -w / 2, -h / 2, w, h / 3.2f);
                        }
                    }
                }
                finally { g.Restore(s); }
            }
        }

        private void DrawSparkles(Graphics g, Rectangle bounds, double now)
        {
            foreach (var s in sparkles)
            {
                double t = now - s.Start;
                if (t < 0 || t > s.Duration) continue;
                double local = t / s.Duration;
                // Twinkle: fade in/out via sine-pulse
                double pulse = Math.Sin(local * Math.PI);
                int alpha = (int)(255 * pulse);
                if (alpha <= 8) continue;

                using (var p = new Pen(Color.FromArgb(alpha, s.Color), 1.6f))
                {
                    float L = s.Size;
                    g.DrawLine(p, s.X - L, s.Y, s.X + L, s.Y);
                    g.DrawLine(p, s.X, s.Y - L, s.X, s.Y + L);
                }
                using (var p2 = new Pen(Color.FromArgb((int)(alpha * 0.6), s.Color), 1.0f))
                {
                    float L = s.Size * 0.7f;
                    g.DrawLine(p2, s.X - L, s.Y - L, s.X + L, s.Y + L);
                    g.DrawLine(p2, s.X + L, s.Y - L, s.X - L, s.Y + L);
                }
                // Bright center dot
                using (var dot = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                    g.FillEllipse(dot, s.X - 1.5f, s.Y - 1.5f, 3f, 3f);
            }
        }

        private static Color LightenColor(Color c, float amount)
        {
            // amount in [0,1] — 0 = unchanged, 1 = white
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(c.A,
                (int)(c.R + (255 - c.R) * amount),
                (int)(c.G + (255 - c.G) * amount),
                (int)(c.B + (255 - c.B) * amount));
        }

        private static Color DarkenColor(Color c, float factor)
        {
            // factor in [0,1] — 1 = unchanged, 0 = black
            factor = Math.Max(0, Math.Min(1, factor));
            return Color.FromArgb(c.A,
                (int)(c.R * factor),
                (int)(c.G * factor),
                (int)(c.B * factor));
        }
    }

    // Expanding ring centered on the screen with optional "Wow!" text.
    private class SurprisedRingEffect : Effect
    {
        private readonly Color color;
        public SurprisedRingEffect(Color color) { this.color = color; LifetimeSec = 1.6; }

        public override void Draw(Graphics g, Rectangle bounds)
        {
            double t = Age;
            double r = 40 + 320 * t; // grows outward
            int alpha = Fade(Progress, 0.05, 0.5);
            float cx = bounds.Width / 2f;
            float cy = bounds.Height / 2f;
            using (var pen = new Pen(Color.FromArgb(alpha, color), 6))
                g.DrawEllipse(pen, (float)(cx - r), (float)(cy - r), (float)(r * 2), (float)(r * 2));
        }
    }

    // Slow blue droplets falling down the screen edges.
    private class SadDropsEffect : Effect
    {
        private struct Drop
        {
            public float X, Vy, Radius;
            public double Delay;
            public Color Color;
        }
        private readonly List<Drop> drops = new List<Drop>();

        public SadDropsEffect(int count, Random rng)
        {
            LifetimeSec = 5.0;
            for (int i = 0; i < count; i++)
            {
                drops.Add(new Drop
                {
                    X = (float)(80 + rng.NextDouble() * 1100),
                    Vy = (float)(60 + rng.NextDouble() * 50),
                    Radius = 5 + (float)rng.NextDouble() * 4,
                    Delay = rng.NextDouble() * 1.5,
                    Color = Color.FromArgb(255, 95, 150, 210)
                });
            }
        }

        public override void Draw(Graphics g, Rectangle bounds)
        {
            double now = Age;
            foreach (var d in drops)
            {
                double t = now - d.Delay;
                if (t < 0) continue;
                float y = 60 + d.Vy * (float)t;
                if (y > bounds.Height) continue;
                int alpha = Fade(Math.Min(1.0, t / (LifetimeSec - d.Delay)), 0.1, 0.7);
                using (var b = new SolidBrush(Color.FromArgb(alpha, d.Color)))
                {
                    g.FillEllipse(b, d.X - d.Radius, y - d.Radius, d.Radius * 2, d.Radius * 2.4f);
                    // Tail
                    g.FillEllipse(b, d.X - d.Radius * 0.5f, y - d.Radius * 3,
                        d.Radius, d.Radius * 1.5f);
                }
            }
        }
    }

    // A pill-shaped text toast — used for Senior gentle messages, Adult corner
    // emojis, and Child "Yay!" notifications.
    private class TextToastEffect : Effect
    {
        public enum ToastPosition { CenterBottom, BottomRight, TopCenter }
        private readonly string text;
        private readonly Color accent;
        private readonly ToastPosition position;
        private readonly float fontSize;

        public TextToastEffect(string text, Color accent, double lifetime,
            ToastPosition position = ToastPosition.CenterBottom, float fontSize = 16f)
        {
            this.text = text;
            this.accent = accent;
            this.position = position;
            this.fontSize = fontSize;
            LifetimeSec = lifetime;
        }

        public override void Draw(Graphics g, Rectangle bounds)
        {
            int alpha = Fade(Progress, 0.1, 0.75);
            using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold))
            {
                SizeF sz = g.MeasureString(text, font);
                int padX = 22, padY = 12;
                int w = (int)sz.Width + padX * 2;
                int h = (int)sz.Height + padY * 2;
                int x, y;
                switch (position)
                {
                    case ToastPosition.BottomRight:
                        x = bounds.Right - w - 30; y = bounds.Bottom - h - 110; break;
                    case ToastPosition.TopCenter:
                        x = (bounds.Width - w) / 2; y = 120; break;
                    default:
                        x = (bounds.Width - w) / 2; y = bounds.Bottom - h - 110; break;
                }
                Rectangle pill = new Rectangle(x, y, w, h);
                // Rounded fill (manual since this class can't call form helpers easily)
                using (var path = new GraphicsPath())
                {
                    int r = h / 2;
                    path.AddArc(pill.X, pill.Y, r * 2, r * 2, 90, 180);
                    path.AddArc(pill.Right - r * 2, pill.Y, r * 2, r * 2, 270, 180);
                    path.CloseFigure();
                    using (var bg = new SolidBrush(Color.FromArgb((int)(alpha * 0.95), accent)))
                        g.FillPath(bg, path);
                }
                using (var tb = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                    g.DrawString(text, font, tb, x + padX, y + padY);
            }
        }
    }

    // Semi-transparent calming wash + centered "Need a break?" text.
    private class CalmingOverlayEffect : Effect
    {
        private readonly string text;
        private readonly Color tint;
        public CalmingOverlayEffect(string text, Color tint)
        {
            this.text = text;
            this.tint = tint;
            LifetimeSec = 5.0;
        }
        public override void Draw(Graphics g, Rectangle bounds)
        {
            int alpha = Fade(Progress, 0.2, 0.7);
            int wash = (int)(alpha * 0.35);
            using (var b = new SolidBrush(Color.FromArgb(wash, tint)))
                g.FillRectangle(b, bounds);
            using (var font = new Font("Segoe UI", 22f, FontStyle.Bold))
            {
                SizeF sz = g.MeasureString(text, font);
                using (var tb = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                    g.DrawString(text, font, tb,
                        bounds.X + (bounds.Width - sz.Width) / 2,
                        bounds.Y + (bounds.Height - sz.Height) / 2);
            }
        }
    }

    private class EmotionEffectEngine
    {
        private readonly List<Effect> active = new List<Effect>();
        private readonly object lockObj = new object();
        private readonly Random rng = new Random();

        // Confirmation tracking
        private string lastSeenEmotion = "";
        private int confirmCount = 0;

        // Cooldowns
        private DateTime globalCooldownUntil = DateTime.MinValue;
        private readonly Dictionary<string, DateTime> perEmotionCooldown
            = new Dictionary<string, DateTime>();

        // Tuned per age mode in Configure(...)
        public int ConfirmationRequired { get; private set; } = 2;
        public int GlobalCooldownSec { get; private set; } = 20;
        public int SameEmotionCooldownSec { get; private set; } = 45;
        public int MaxConcurrent { get; private set; } = 1;
        public UIMode Mode { get; private set; } = UIMode.Adult;
        public Color AccentColor { get; set; } = Color.DodgerBlue;

        public void Configure(UIMode mode, Color accent)
        {
            Mode = mode;
            AccentColor = accent;
            // Single simple rule for every age: fire on first detection, then
            // a global 10-second cooldown before any other animation can run.
            // (Per-mode tuning still applies to *which* effect we spawn —
            //  balloons for Child, plain toast for Senior, etc.)
            ConfirmationRequired   = 1;
            GlobalCooldownSec      = 10;
            SameEmotionCooldownSec = 10; // same as global → no extra per-emotion gating
            MaxConcurrent          = 3; // each spawn now adds 2 effects (visual + toast); allow headroom

            // Clear cooldown state on mode change
            globalCooldownUntil = DateTime.MinValue;
            perEmotionCooldown.Clear();
            lastSeenEmotion = "";
            confirmCount = 0;
        }

        public bool HasActiveEffects
        {
            get { lock (lockObj) { return active.RemoveAll(e => !e.IsAlive) > -1 && active.Count > 0; } }
        }

        // Called from the message loop with the parsed emotion word.
        public void OnEmotionEvent(string emotionRaw)
        {
            string emotion = (emotionRaw ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(emotion) || emotion == "neutral") return;
            if (!IsKnownEmotion(emotion)) return;

            bool isSpike = (emotion == "surprised" || emotion == "surprise");

            if (!isSpike)
            {
                if (emotion != lastSeenEmotion)
                {
                    lastSeenEmotion = emotion;
                    confirmCount = 1;
                }
                else
                {
                    confirmCount++;
                }
                if (confirmCount < ConfirmationRequired) return;
            }

            DateTime now = DateTime.Now;
            if (now < globalCooldownUntil) return;
            DateTime untilDt;
            if (perEmotionCooldown.TryGetValue(emotion, out untilDt) && now < untilDt) return;

            lock (lockObj)
            {
                active.RemoveAll(e => !e.IsAlive);
                if (active.Count >= MaxConcurrent) return;
            }

            Spawn(emotion);
            globalCooldownUntil = now.AddSeconds(GlobalCooldownSec);
            perEmotionCooldown[emotion] = now.AddSeconds(SameEmotionCooldownSec);

            if (!isSpike) { confirmCount = 0; lastSeenEmotion = ""; }
        }

        private bool IsKnownEmotion(string e)
        {
            return e == "happy" || e == "surprised" || e == "surprise"
                || e == "sad" || e == "angry" || e == "anger"
                || e == "fear" || e == "fearful";
        }

        private void Spawn(string emotion)
        {
            lock (lockObj)
            {
                if (emotion == "happy") SpawnHappy();
                else if (emotion == "surprised" || emotion == "surprise") SpawnSurprised();
                else if (emotion == "sad") SpawnSad();
                else SpawnCalming(emotion);
            }
        }

        private static readonly Color[] BalloonPalette = {
            Color.FromArgb(255, 110, 130),
            Color.FromArgb(135, 201, 255),
            Color.FromArgb(255, 219, 102),
            Color.FromArgb(160, 220, 130),
            Color.FromArgb(196, 174, 240),
            Color.FromArgb(255, 165, 102),
        };

        // All ages now get the full "Child" celebration treatment — the rich
        // multi-layer balloon/confetti/sparkle scene + accompanying toasts.
        private void SpawnHappy()
        {
            active.Add(new BalloonBurstEffect(10, 32, BalloonPalette, rng));
            active.Add(new TextToastEffect("Yay! 🎉",
                Color.FromArgb(255, 110, 175), 2.5,
                TextToastEffect.ToastPosition.TopCenter, 22f));
        }

        private void SpawnSurprised()
        {
            active.Add(new SurprisedRingEffect(AccentColor));
            active.Add(new TextToastEffect("Wow!",
                AccentColor, 1.6,
                TextToastEffect.ToastPosition.TopCenter, 28f));
        }

        private void SpawnSad()
        {
            active.Add(new SadDropsEffect(6, rng));
            active.Add(new TextToastEffect("Want to try favourites?",
                Color.FromArgb(255, 95, 150, 210), 3.5,
                TextToastEffect.ToastPosition.TopCenter, 14f));
        }

        private void SpawnCalming(string emotion)
        {
            string text = (emotion == "angry" || emotion == "anger")
                ? "Need a break?"
                : "It's okay, take your time.";
            Color tint = Color.FromArgb(80, 100, 130);
            active.Add(new CalmingOverlayEffect(text, tint));
        }

        public void DrawAll(Graphics g, Rectangle bounds)
        {
            List<Effect> snapshot;
            lock (lockObj)
            {
                active.RemoveAll(e => !e.IsAlive);
                snapshot = new List<Effect>(active);
            }
            foreach (Effect e in snapshot)
            {
                try { e.Draw(g, bounds); } catch { }
            }
        }
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

internal interface IAdminGestureReceiver
{
    bool HandleGestureCommand(string gesture);
}

public sealed class GestureTextEntryForm : Form, IAdminGestureReceiver
{
    private static readonly string[] Tokens = new[]
    {
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J",
        "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T",
        "U", "V", "W", "X", "Y", "Z", "0", "1", "2", "3",
        "4", "5", "6", "7", "8", "9", "SPACE", "BACK", "DONE", "CANCEL"
    };

    private readonly Label promptLabel;
    private readonly Label bufferLabel;
    private readonly Label tokenLabel;
    private readonly Label hintLabel;
    private int tokenIndex;
    private string buffer;

    public string ResultText { get { return buffer; } }
    public bool WasCancelled { get; private set; }

    public GestureTextEntryForm(string prompt, string initialText)
    {
        Text = "Gesture Input";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(760, 260);
        BackColor = Color.FromArgb(241, 245, 250);

        buffer = initialText ?? string.Empty;

        promptLabel = new Label
        {
            Text = prompt,
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 20)
        };

        bufferLabel = new Label
        {
            Text = buffer.Length > 0 ? buffer : "<empty>",
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            AutoSize = false,
            Width = 710,
            Height = 70,
            Location = new Point(24, 72),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Padding = new Padding(12)
        };

        tokenLabel = new Label
        {
            Text = CurrentTokenText(),
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            AutoSize = false,
            Width = 220,
            Height = 64,
            Location = new Point(24, 154),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(223, 235, 255),
            Padding = new Padding(10)
        };

        hintLabel = new Label
        {
            Text = "Swipe Left/Right = change token   Circle = select   Mute = backspace   DarkMode = done",
            Font = new Font("Segoe UI", 10f),
            AutoSize = true,
            Location = new Point(24, 226),
            ForeColor = Color.FromArgb(88, 98, 112)
        };

        Controls.Add(promptLabel);
        Controls.Add(bufferLabel);
        Controls.Add(tokenLabel);
        Controls.Add(hintLabel);
    }

    private string CurrentTokenText()
    {
        return "Token: " + Tokens[tokenIndex];
    }

    private void RefreshView()
    {
        bufferLabel.Text = buffer.Length > 0 ? buffer : "<empty>";
        tokenLabel.Text = CurrentTokenText();
    }

    private void AppendToken(string token)
    {
        if (token == "SPACE") buffer += " ";
        else buffer += token;
    }

    public bool HandleGestureCommand(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return false;

        if (gesture == "SwipeRight")
        {
            tokenIndex = (tokenIndex + 1) % Tokens.Length;
            RefreshView();
            return true;
        }

        if (gesture == "SwipeLeft")
        {
            tokenIndex = (tokenIndex - 1 + Tokens.Length) % Tokens.Length;
            RefreshView();
            return true;
        }

        if (gesture == "Circle")
        {
            string token = Tokens[tokenIndex];
            if (token == "BACK")
            {
                if (buffer.Length > 0) buffer = buffer.Substring(0, buffer.Length - 1);
            }
            else if (token == "DONE")
            {
                DialogResult = DialogResult.OK;
                Close();
                return true;
            }
            else if (token == "CANCEL")
            {
                WasCancelled = true;
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            else
            {
                AppendToken(token);
            }

            RefreshView();
            return true;
        }

        if (gesture == "Mute")
        {
            if (buffer.Length > 0)
            {
                buffer = buffer.Substring(0, buffer.Length - 1);
                RefreshView();
            }
            return true;
        }

        if (gesture == "DarkMode")
        {
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }

        return false;
    }
}

public sealed class GestureListPickerForm : Form, IAdminGestureReceiver
{
    private readonly string[] options;
    private readonly Label promptLabel;
    private readonly Label optionLabel;
    private readonly Label hintLabel;
    private int optionIndex;

    public string ResultChoice { get; private set; }
    public bool WasCancelled { get; private set; }

    public GestureListPickerForm(string prompt, string[] choices, string currentValue)
    {
        options = choices ?? Array.Empty<string>();
        if (options.Length == 0)
        {
            options = new[] { "" };
        }

        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            for (int i = 0; i < options.Length; i++)
            {
                if (string.Equals(options[i], currentValue, StringComparison.OrdinalIgnoreCase))
                {
                    optionIndex = i;
                    break;
                }
            }
        }

        Text = "Gesture Picker";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(760, 220);
        BackColor = Color.FromArgb(241, 245, 250);

        promptLabel = new Label
        {
            Text = prompt,
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 18)
        };

        optionLabel = new Label
        {
            Text = CurrentOptionText(),
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            AutoSize = false,
            Width = 710,
            Height = 70,
            Location = new Point(24, 64),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Padding = new Padding(12)
        };

        hintLabel = new Label
        {
            Text = "Swipe Left/Right = change option   Circle = select   Mute = cancel",
            Font = new Font("Segoe UI", 10f),
            AutoSize = true,
            Location = new Point(24, 150),
            ForeColor = Color.FromArgb(88, 98, 112)
        };

        Controls.Add(promptLabel);
        Controls.Add(optionLabel);
        Controls.Add(hintLabel);
    }

    private string CurrentOptionText()
    {
        return "Option: " + options[optionIndex];
    }

    private void RefreshView()
    {
        optionLabel.Text = CurrentOptionText();
    }

    public bool HandleGestureCommand(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return false;

        if (gesture == "SwipeRight")
        {
            optionIndex = (optionIndex + 1) % options.Length;
            RefreshView();
            return true;
        }

        if (gesture == "SwipeLeft")
        {
            optionIndex = (optionIndex - 1 + options.Length) % options.Length;
            RefreshView();
            return true;
        }

        if (gesture == "Circle")
        {
            ResultChoice = options[optionIndex];
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }

        if (gesture == "Mute")
        {
            WasCancelled = true;
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }

        return false;
    }
}

public sealed class AdminLoginForm : Form
{
    private readonly TextBox usernameTextBox;
    private readonly TextBox passwordTextBox;

    public string EnteredUsername { get { return (usernameTextBox.Text ?? string.Empty).Trim(); } }
    public string EnteredPassword { get { return passwordTextBox.Text ?? string.Empty; } }

    public AdminLoginForm()
    {
        Text = "Admin Authentication";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 230);
        BackColor = Color.FromArgb(245, 248, 252);

        var title = new Label
        {
            Text = "MuseSense Admin Portal",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 18),
            ForeColor = Color.FromArgb(24, 31, 42)
        };

        var subtitle = new Label
        {
            Text = "Enter administrator credentials to continue.",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(26, 50),
            ForeColor = Color.FromArgb(88, 98, 112)
        };

        var userLabel = new Label
        {
            Text = "Username",
            AutoSize = true,
            Location = new Point(28, 86),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };

        usernameTextBox = new TextBox
        {
            Location = new Point(28, 106),
            Width = 380,
            Font = new Font("Segoe UI", 10f)
        };

        var passwordLabel = new Label
        {
            Text = "Password",
            AutoSize = true,
            Location = new Point(28, 138),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };

        passwordTextBox = new TextBox
        {
            Location = new Point(28, 158),
            Width = 380,
            Font = new Font("Segoe UI", 10f),
            UseSystemPasswordChar = true
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Width = 94,
            Height = 34,
            Location = new Point(214, 188),
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        var loginButton = new Button
        {
            Text = "Sign In",
            Width = 94,
            Height = 34,
            Location = new Point(314, 188),
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            BackColor = Color.FromArgb(18, 124, 255),
            ForeColor = Color.White
        };

        AcceptButton = loginButton;
        CancelButton = cancelButton;

        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(userLabel);
        Controls.Add(usernameTextBox);
        Controls.Add(passwordLabel);
        Controls.Add(passwordTextBox);
        Controls.Add(cancelButton);
        Controls.Add(loginButton);
    }
}

public sealed class AdminArtifact
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
    public string category { get; set; }
    public string tags { get; set; }
    public string historicalInfo { get; set; }
    public string period { get; set; }
}

internal sealed class AdminArtifactRoot
{
    public List<AdminArtifact> artifacts { get; set; }
}

public sealed class ArtifactTemplate
{
    public string Name { get; set; }
    public string BirthDate { get; set; }
    public string Era { get; set; }
    public string Origin { get; set; }
    public string Description { get; set; }
    public string Narration { get; set; }
    public string ObjPath { get; set; }
    public string AudioPath { get; set; }
    public string Color { get; set; }
    public string Country { get; set; }
    public string Category { get; set; }
    public string Tags { get; set; }
    public string HistoricalInfo { get; set; }
    public string Period { get; set; }

    public AdminArtifact ToArtifact(int id, int tuioId)
    {
        return new AdminArtifact
        {
            id = id,
            tuioId = tuioId,
            name = Name,
            birthDate = BirthDate,
            era = Era,
            origin = Origin,
            description = Description,
            narration = Narration,
            objPath = ObjPath,
            audioPath = AudioPath,
            color = Color,
            country = Country,
            category = Category,
            tags = Tags,
            historicalInfo = HistoricalInfo,
            period = Period
        };
    }
}

internal static class ArtifactTemplateStore
{
    public static readonly ArtifactTemplate[] Templates = new[]
    {
        new ArtifactTemplate
        {
            Name = "Cleopatra VII",
            BirthDate = "69\u201330 BC",
            Era = "Ptolemaic Period",
            Origin = "Alexandria, Egypt",
            Description = "Cleopatra VII was the last active ruler of the Ptolemaic Kingdom of Egypt. Renowned for her political acumen, multilingual diplomacy, and strategic alliances with Julius Caesar and Mark Antony, she remains one of history\u2019s most iconic figures.",
            Narration = "Here you see Cleopatra VII, the last queen of Egypt\u2019s Ptolemaic dynasty. She ruled from 51 to 30 BC and was the first Ptolemaic ruler to learn the Egyptian language. Known for her intelligence, charisma, and political ambition, she formed powerful alliances with Rome\u2019s most influential leaders to preserve Egypt\u2019s independence. Her dramatic life and death alongside Mark Antony have inspired countless stories, artworks, and films throughout history.",
            ObjPath = "artifacts/cleopatra.png",
            AudioPath = "audio/cleopatra.wav",
            Color = "#D4A017",
            Country = "Egypt",
            Category = "Historical Figure",
            Tags = "queen, ptolemaic, egypt, ruler, hellenistic, cleopatra",
            HistoricalInfo = "Here you see Cleopatra VII, the last queen of Egypt\u2019s Ptolemaic dynasty. She ruled from 51 to 30 BC and was the first Ptolemaic ruler to learn the Egyptian language. Known for her intelligence, charisma, and political ambition, she formed powerful alliances with Rome\u2019s most influential leaders to preserve Egypt\u2019s independence.",
            Period = "69\u201330 BC"
        },
        new ArtifactTemplate
        {
            Name = "Anubis",
            BirthDate = "Worshipped from c. 3100 BC",
            Era = "All periods",
            Origin = "Ancient Egypt",
            Description = "Anubis is the ancient Egyptian god of mummification, tombs, and the afterlife. Depicted as a black jackal or a human with a jackal head, he guided souls through the underworld and presided over the embalming ritual.",
            Narration = "This representation shows Anubis, one of the most recognizable deities in the Egyptian pantheon. As the god of embalming and guardian of the dead, Anubis played a central role in Egyptian funerary practices. Priests performing mummification often wore jackal-headed masks to invoke his protection. Anubis was believed to oversee the Weighing of the Heart ceremony, where the deceased\u2019s heart was balanced against the feather of Ma\u2019at to determine their worthiness for the afterlife.",
            ObjPath = "artifacts/Anubis.png",
            AudioPath = "audio/anubis.wav",
            Color = "#1A1A1A",
            Country = "Egypt",
            Category = "Deity",
            Tags = "god, jackal, afterlife, mythology, mummification",
            HistoricalInfo = "This representation shows Anubis, one of the most recognizable deities in the Egyptian pantheon. As the god of embalming and guardian of the dead, Anubis played a central role in Egyptian funerary practices. Priests performing mummification often wore jackal-headed masks to invoke his protection.",
            Period = "Worshipped from c. 3100 BC"
        },
        new ArtifactTemplate
        {
            Name = "Rosetta Stone",
            BirthDate = "196 BC",
            Era = "Ptolemaic Period",
            Origin = "Rosetta (Rashid), Egypt",
            Description = "The Rosetta Stone is a granodiorite stele inscribed with a decree issued in 196 BC during the reign of King Ptolemy V. Its parallel texts in Egyptian hieroglyphs, Demotic script, and Ancient Greek provided the key to deciphering Egyptian hieroglyphs.",
            Narration = "The Rosetta Stone is one of the most significant archaeological finds in history. Discovered in 1799 by French soldiers during Napoleon\u2019s Egyptian campaign, this granodiorite slab carries the same decree in three scripts: hieroglyphic for temple inscriptions, Demotic for everyday use, and Ancient Greek for the administration. The breakthrough came in 1822 when Jean-Fran\u00e7ois Champollion recognized that hieroglyphs were not purely symbolic but included phonetic characters, allowing him to unlock the written language of ancient Egypt.",
            ObjPath = "artifacts/Rosetta Stone.png",
            AudioPath = "audio/rosetta_stone.wav",
            Color = "#6B5B4F",
            Country = "Egypt",
            Category = "Inscription",
            Tags = "stele, hieroglyphs, decipherment, ptolemaic, inscription",
            HistoricalInfo = "The Rosetta Stone is one of the most significant archaeological finds in history. Discovered in 1799 by French soldiers during Napoleon\u2019s Egyptian campaign, this granodiorite slab carries the same decree in three scripts: hieroglyphic for temple inscriptions, Demotic for everyday use, and Ancient Greek for the administration.",
            Period = "196 BC"
        }
    };
}

public sealed class AdminTemplateBrowserForm : Form, IAdminGestureReceiver
{
    private readonly ArtifactTemplate[] templates;
    private int templateIndex;

    private readonly PictureBox imageBox;
    private readonly Label counterLabel;
    private readonly Label nameLabel;
    private readonly Label eraLabel;
    private readonly Label originLabel;
    private readonly Label descriptionLabel;
    private readonly Label categoryLabel;
    private readonly Label tagsLabel;
    private readonly Label hintLabel;

    public AdminArtifact ResultArtifact { get; private set; }
    public bool WasCancelled { get; private set; }

    public AdminTemplateBrowserForm()
    {
        templates = ArtifactTemplateStore.Templates;

        Text = "Create Artifact from Template";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(940, 620);
        BackColor = Color.FromArgb(20, 24, 34);

        var imagePanel = new Panel
        {
            Location = new Point(24, 24),
            Size = new Size(400, 460),
            BackColor = Color.FromArgb(30, 35, 48),
            BorderStyle = BorderStyle.FixedSingle
        };

        imageBox = new PictureBox
        {
            Location = new Point(0, 0),
            Size = new Size(400, 460),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(30, 35, 48)
        };
        imagePanel.Controls.Add(imageBox);

        int detailX = 450;

        counterLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(detailX, 28),
            ForeColor = Color.FromArgb(140, 152, 175)
        };

        nameLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            AutoSize = false,
            Width = 460,
            Height = 60,
            Location = new Point(detailX, 54),
            ForeColor = Color.White
        };

        categoryLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 10f),
            AutoSize = true,
            Location = new Point(detailX, 118),
            ForeColor = Color.FromArgb(100, 180, 255)
        };

        eraLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 10f),
            AutoSize = true,
            Location = new Point(detailX, 144),
            ForeColor = Color.FromArgb(160, 172, 190)
        };

        originLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 10f),
            AutoSize = true,
            Location = new Point(detailX, 170),
            ForeColor = Color.FromArgb(160, 172, 190)
        };

        descriptionLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = false,
            Width = 460,
            Height = 160,
            Location = new Point(detailX, 200),
            ForeColor = Color.FromArgb(210, 215, 225)
        };

        tagsLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 9f, FontStyle.Italic),
            AutoSize = true,
            Location = new Point(detailX, 370),
            ForeColor = Color.FromArgb(120, 132, 150)
        };

        hintLabel = new Label
        {
            Text = "Navigate: Swipe Left/Right or AdminNext/Prev   |   Create: AdminCreateArtifact or Circle   |   Cancel: Mute",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 570),
            ForeColor = Color.FromArgb(140, 152, 175)
        };

        Controls.Add(imagePanel);
        Controls.Add(counterLabel);
        Controls.Add(nameLabel);
        Controls.Add(categoryLabel);
        Controls.Add(eraLabel);
        Controls.Add(originLabel);
        Controls.Add(descriptionLabel);
        Controls.Add(tagsLabel);
        Controls.Add(hintLabel);

        ShowCurrentTemplate();
    }

    private string ResolveFullImagePath(string objPath)
    {
        if (string.IsNullOrWhiteSpace(objPath)) return null;
        string baseDir = Path.GetDirectoryName(Application.ExecutablePath);
        string full = Path.Combine(baseDir, objPath);
        if (File.Exists(full)) return full;
        return null;
    }

    private void ShowCurrentTemplate()
    {
        var t = templates[templateIndex];
        counterLabel.Text = string.Format("Template {0} of {1}", templateIndex + 1, templates.Length);
        nameLabel.Text = t.Name ?? "";
        categoryLabel.Text = t.Category ?? "";
        eraLabel.Text = "Era: " + (string.IsNullOrWhiteSpace(t.Era) ? "\u2014" : t.Era);
        originLabel.Text = "Origin: " + (string.IsNullOrWhiteSpace(t.Origin) ? "\u2014" : t.Origin);
        descriptionLabel.Text = t.Description ?? "";
        tagsLabel.Text = "Tags: " + (string.IsNullOrWhiteSpace(t.Tags) ? "none" : t.Tags);

        string imagePath = ResolveFullImagePath(t.ObjPath);
        if (imagePath != null)
        {
            try { imageBox.Image = Image.FromFile(imagePath); }
            catch { imageBox.Image = null; }
        }
        else
        {
            imageBox.Image = null;
        }
    }

    private void MoveTemplate(int delta)
    {
        if (templates.Length == 0) return;
        templateIndex = (templateIndex + delta + templates.Length) % templates.Length;
        ShowCurrentTemplate();
    }

    private void ConfirmTemplate()
    {
        var t = templates[templateIndex];
        ResultArtifact = t.ToArtifact(0, 0);
        DialogResult = DialogResult.OK;
        Close();
    }

    public bool HandleGestureCommand(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        string g = gesture.Trim();
        string gl = g.ToLowerInvariant();

        if (gl == "adminnextartifact" || gl == "adminnext" || gl == "nextartifact" || gl == "swipeleft")
        {
            MoveTemplate(1);
            return true;
        }

        if (gl == "adminprevartifact" || gl == "adminprev" || gl == "prevartifact" || gl == "swiperight")
        {
            MoveTemplate(-1);
            return true;
        }

        if (gl == "admincreateartifact" || gl == "admincreate" || gl == "createartifact" || gl == "circle")
        {
            ConfirmTemplate();
            return true;
        }

        if (gl == "admindeleteartifact" || gl == "admindelete" || gl == "deleteartifact" || gl == "mute")
        {
            WasCancelled = true;
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }

        return false;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        if (imageBox.Image != null)
        {
            imageBox.Image.Dispose();
            imageBox.Image = null;
        }
    }
}

public sealed class AdminArtifactEditorForm : Form, IAdminGestureReceiver
{
    private static readonly string[] ImageOptions = new[]
    {
        "artifacts/Tutankhamun.png",
        "artifacts/Ramses II.png",
        "artifacts/King Senwosret III.png",
        "artifacts/Bust of Nefertiti.png"
    };
    private readonly TextBox idBox;
    private readonly TextBox tuioIdBox;
    private readonly TextBox nameBox;
    private readonly TextBox descriptionBox;
    private readonly TextBox categoryBox;
    private readonly TextBox historicalInfoBox;
    private readonly TextBox tagsBox;
    private readonly TextBox periodBox;
    private readonly TextBox birthDateBox;
    private readonly TextBox eraBox;
    private readonly TextBox originBox;
    private readonly TextBox countryBox;
    private readonly TextBox objPathBox;
    private readonly TextBox audioPathBox;
    private readonly TextBox colorBox;
    private readonly TextBox narrationBox;
    private readonly TextBox[] editableFields;
    private readonly string[] editableFieldNames;
    private readonly Label gestureHintLabel;
    private int activeFieldIndex;

    public AdminArtifact Artifact { get; private set; }

    public AdminArtifactEditorForm(AdminArtifact source)
    {
        var artifact = source ?? new AdminArtifact();

        Text = source == null ? "Create Artifact" : "Edit Artifact";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(850, 720);

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(248, 250, 255)
        };
        Controls.Add(panel);

        int leftX = 24;
        int rightX = 430;
        int y = 24;

        idBox = AddField(panel, "Artifact ID", artifact.id.ToString(), leftX, ref y);
        tuioIdBox = AddField(panel, "TUIO ID", artifact.tuioId.ToString(), rightX, ref y);
        nameBox = AddField(panel, "Title", artifact.name, leftX, ref y);
        categoryBox = AddField(panel, "Category", string.IsNullOrWhiteSpace(artifact.category) ? artifact.country : artifact.category, rightX, ref y);
        descriptionBox = AddMultiLineField(panel, "Description", artifact.description, leftX, 90, ref y);
        historicalInfoBox = AddMultiLineField(panel, "Historical Information", string.IsNullOrWhiteSpace(artifact.historicalInfo) ? artifact.narration : artifact.historicalInfo, rightX, 90, ref y);
        tagsBox = AddField(panel, "Tags (comma-separated)", artifact.tags, leftX, ref y);
        periodBox = AddField(panel, "Date/Period", string.IsNullOrWhiteSpace(artifact.period) ? artifact.birthDate : artifact.period, rightX, ref y);
        birthDateBox = AddField(panel, "Birth Date", artifact.birthDate, leftX, ref y);
        eraBox = AddField(panel, "Era", artifact.era, rightX, ref y);
        originBox = AddField(panel, "Origin", artifact.origin, leftX, ref y);
        countryBox = AddField(panel, "Country", artifact.country, rightX, ref y);
        objPathBox = AddField(panel, "Image/Model Path", artifact.objPath, leftX, ref y);
        audioPathBox = AddField(panel, "Audio Path", artifact.audioPath, rightX, ref y);
        colorBox = AddField(panel, "Theme Color", artifact.color, leftX, ref y);
        narrationBox = AddMultiLineField(panel, "Narration", artifact.narration, rightX, 110, ref y);

        editableFields = new[]
        {
            idBox, tuioIdBox, nameBox, categoryBox, descriptionBox, historicalInfoBox,
            tagsBox, periodBox, birthDateBox, eraBox, originBox, countryBox,
            objPathBox, audioPathBox, colorBox, narrationBox
        };
        editableFieldNames = new[]
        {
            "Artifact ID", "TUIO ID", "Title", "Category", "Description",
            "Historical Information", "Tags", "Date/Period", "Birth Date", "Era",
            "Origin", "Country", "Image/Model Path", "Audio Path", "Theme Color", "Narration"
        };

        foreach (var field in editableFields)
        {
            field.BackColor = Color.White;
        }

        gestureHintLabel = new Label
        {
            Text = "Gesture controls: Swipe Left/Right = field, Circle = edit, Mute = cancel, DarkMode = save",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(88, 98, 112),
            Location = new Point(24, 12)
        };
        panel.Controls.Add(gestureHintLabel);
        HighlightActiveField();

        var cancelButton = new Button
        {
            Text = "Cancel",
            Width = 110,
            Height = 34,
            Location = new Point(590, 650),
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat
        };

        var saveButton = new Button
        {
            Text = source == null ? "Create Artifact" : "Save Changes",
            Width = 170,
            Height = 34,
            Location = new Point(686, 650),
            DialogResult = DialogResult.None,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(18, 124, 255),
            ForeColor = Color.White
        };

        saveButton.Click += (s, e) =>
        {
            if (TryCommitArtifact())
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        panel.Controls.Add(cancelButton);
        panel.Controls.Add(saveButton);
    }

    private void HighlightActiveField()
    {
        for (int i = 0; i < editableFields.Length; i++)
        {
            editableFields[i].BackColor = i == activeFieldIndex ? Color.FromArgb(223, 235, 255) : Color.White;
        }

        string extraHint = editableFields[activeFieldIndex] == objPathBox
            ? "   |   Image picker: Swipe Left/Right = option   Circle = select"
            : string.Empty;
        gestureHintLabel.Text = "Editing: " + editableFieldNames[activeFieldIndex] + "   |   Swipe Left/Right = field   Circle = edit   Mute = cancel   DarkMode = save" + extraHint;
    }

    private void MoveField(int delta)
    {
        activeFieldIndex = (activeFieldIndex + delta) % editableFields.Length;
        if (activeFieldIndex < 0)
        {
            activeFieldIndex += editableFields.Length;
        }

        HighlightActiveField();
    }

    private bool TryEditActiveField()
    {
        string prompt = "Enter " + editableFieldNames[activeFieldIndex];
        string initial = editableFields[activeFieldIndex].Text ?? string.Empty;

        if (editableFields[activeFieldIndex] == objPathBox)
        {
            using (var picker = new GestureListPickerForm("Choose image/model", ImageOptions, initial))
            {
                if (picker.ShowDialog(this) != DialogResult.OK || picker.WasCancelled)
                {
                    return false;
                }

                editableFields[activeFieldIndex].Text = picker.ResultChoice;
            }

            return true;
        }

        using (var input = new GestureTextEntryForm(prompt, initial))
        {
            if (input.ShowDialog(this) != DialogResult.OK || input.WasCancelled)
            {
                return false;
            }

            editableFields[activeFieldIndex].Text = input.ResultText;
        }

        return true;
    }

    private bool TryCommitArtifact()
    {
        int parsedId;
        int parsedTuioId;
        if (!int.TryParse(idBox.Text.Trim(), out parsedId))
        {
            MessageBox.Show(this, "Artifact ID must be a valid integer.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (!int.TryParse(tuioIdBox.Text.Trim(), out parsedTuioId))
        {
            MessageBox.Show(this, "TUIO ID must be a valid integer.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (string.IsNullOrWhiteSpace(nameBox.Text))
        {
            MessageBox.Show(this, "Title is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        Artifact = new AdminArtifact
        {
            id = parsedId,
            tuioId = parsedTuioId,
            name = nameBox.Text.Trim(),
            description = descriptionBox.Text.Trim(),
            category = categoryBox.Text.Trim(),
            historicalInfo = historicalInfoBox.Text.Trim(),
            tags = tagsBox.Text.Trim(),
            period = periodBox.Text.Trim(),
            birthDate = birthDateBox.Text.Trim(),
            era = eraBox.Text.Trim(),
            origin = originBox.Text.Trim(),
            country = countryBox.Text.Trim(),
            objPath = objPathBox.Text.Trim(),
            audioPath = audioPathBox.Text.Trim(),
            color = colorBox.Text.Trim(),
            narration = narrationBox.Text.Trim()
        };

        return true;
    }

    public bool HandleGestureCommand(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return false;
        }

        string g = gesture.Trim();
        string gl = g.ToLowerInvariant();

        if (gesture == "SwipeRight")
        {
            MoveField(1);
            return true;
        }

        if (gesture == "SwipeLeft")
        {
            MoveField(-1);
            return true;
        }

        if (gesture == "Circle")
        {
            TryEditActiveField();
            return true;
        }

        if (gesture == "Mute")
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }

        if (gesture == "DarkMode")
        {
            if (TryCommitArtifact())
            {
                DialogResult = DialogResult.OK;
                Close();
            }

            return true;
        }

        return false;
    }

    private static TextBox AddField(Control parent, string label, string value, int x, ref int y)
    {
        var labelControl = new Label { Text = label, AutoSize = true, Location = new Point(x, y), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        var box = new TextBox { Text = value ?? string.Empty, Width = 360, Location = new Point(x, y + 20), Font = new Font("Segoe UI", 9.5f) };
        parent.Controls.Add(labelControl);
        parent.Controls.Add(box);
        if (x > 300) y += 58;
        return box;
    }

    private static TextBox AddMultiLineField(Control parent, string label, string value, int x, int height, ref int y)
    {
        var labelControl = new Label { Text = label, AutoSize = true, Location = new Point(x, y), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        var box = new TextBox
        {
            Text = value ?? string.Empty,
            Width = 360,
            Height = height,
            Multiline = true,
            Location = new Point(x, y + 20),
            Font = new Font("Segoe UI", 9.5f),
            ScrollBars = ScrollBars.Vertical
        };
        parent.Controls.Add(labelControl);
        parent.Controls.Add(box);
        if (x > 300) y += height + 36;
        return box;
    }
}

internal sealed class ArtifactAnalytics
{
    public string Name { get; set; }
    public int Views { get; set; }
    public int Interactions { get; set; }
    public double DurationScore { get; set; }
    public double PositiveEmotionScore { get; set; }
    public double FinalScore { get; set; }
}

public sealed class AdminDashboardForm : Form, IAdminGestureReceiver
{
    private readonly string artifactsPath;
    private readonly string contextPath;
    private readonly string reportsPath;
    private readonly Action onArtifactsChanged;

    private readonly DataGridView artifactGrid;
    private readonly TextBox searchBox;
    private readonly ComboBox categoryFilter;
    private readonly Label favorableArtifactLabel;
    private readonly Label scoreReasonLabel;
    private readonly Label sessionsLabel;
    private readonly Label usersLabel;
    private readonly Label averageViewsLabel;
    private readonly ListView analyticsList;
    private readonly Label adminHintLabel;

    private List<AdminArtifact> artifacts = new List<AdminArtifact>();
    private AdminTemplateBrowserForm activeTemplateBrowser;

    public AdminDashboardForm(string artifactsPath, string contextPath, string reportsPath, Action onArtifactsChanged)
    {
        this.artifactsPath = artifactsPath;
        this.contextPath = contextPath;
        this.reportsPath = reportsPath;
        this.onArtifactsChanged = onArtifactsChanged;

        Text = "MuseSense Admin Management";
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        BackColor = Color.FromArgb(241, 245, 250);

        var topPanel = new Panel { Dock = DockStyle.Top, Height = 90, BackColor = Color.White };
        var leftPanel = new Panel { Dock = DockStyle.Left, Width = 840, BackColor = Color.FromArgb(248, 252, 255) };
        var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(246, 249, 252) };

        var title = new Label
        {
            Text = "Admin Dashboard",
            Font = new Font("Segoe UI", 20f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 12)
        };
        var subtitle = new Label
        {
            Text = "Artifact CRUD, session analytics, and favorable artifact ranking",
            Font = new Font("Segoe UI", 10f),
            AutoSize = true,
            Location = new Point(26, 52),
            ForeColor = Color.FromArgb(88, 98, 112)
        };
        topPanel.Controls.Add(title);
        topPanel.Controls.Add(subtitle);

        Controls.Add(rightPanel);
        Controls.Add(leftPanel);
        Controls.Add(topPanel);

        searchBox = new TextBox
        {
            Width = 320,
            Location = new Point(24, 22),
            Font = new Font("Segoe UI", 9.5f),
            Text = "Search title, category, tags, era..."
        };
        searchBox.GotFocus += (s, e) =>
        {
            if (searchBox.Text == "Search title, category, tags, era...")
            {
                searchBox.Text = string.Empty;
            }
        };
        searchBox.LostFocus += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(searchBox.Text))
            {
                searchBox.Text = "Search title, category, tags, era...";
            }
        };
        searchBox.TextChanged += (s, e) => RefreshGrid();

        categoryFilter = new ComboBox
        {
            Width = 190,
            Location = new Point(356, 22),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9.5f)
        };
        categoryFilter.SelectedIndexChanged += (s, e) => RefreshGrid();

        var addButton = BuildActionButton("Create Artifact", new Point(560, 20), Color.FromArgb(24, 147, 78));
        addButton.Click += (s, e) => CreateArtifact();
        var editButton = BuildActionButton("Edit Selected", new Point(692, 20), Color.FromArgb(18, 124, 255));
        editButton.Click += (s, e) => EditSelectedArtifact();
        var deleteButton = BuildActionButton("Delete", new Point(692, 58), Color.FromArgb(212, 69, 77));
        deleteButton.Click += (s, e) => DeleteSelectedArtifact();
        var refreshButton = BuildActionButton("Reload", new Point(560, 58), Color.FromArgb(95, 104, 121));
        refreshButton.Click += (s, e) => ReloadAll();

        artifactGrid = new DataGridView
        {
            Location = new Point(24, 100),
            Width = 792,
            Height = 690,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            Font = new Font("Segoe UI", 9f)
        };
        artifactGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", DataPropertyName = "id", FillWeight = 12 });
        artifactGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "TUIO", DataPropertyName = "tuioId", FillWeight = 12 });
        artifactGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Title", DataPropertyName = "name", FillWeight = 34 });
        artifactGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Category", DataPropertyName = "category", FillWeight = 20 });
        artifactGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Period", DataPropertyName = "period", FillWeight = 18 });
        artifactGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tags", DataPropertyName = "tags", FillWeight = 24 });

        leftPanel.Controls.Add(searchBox);
        leftPanel.Controls.Add(categoryFilter);
        leftPanel.Controls.Add(addButton);
        leftPanel.Controls.Add(editButton);
        leftPanel.Controls.Add(deleteButton);
        leftPanel.Controls.Add(refreshButton);
        leftPanel.Controls.Add(artifactGrid);

        favorableArtifactLabel = new Label
        {
            Text = "Most Favorable Artifact: -",
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 24)
        };
        scoreReasonLabel = new Label
        {
            Text = "Ranking explanation will appear here.",
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            Location = new Point(26, 58),
            ForeColor = Color.FromArgb(73, 84, 99)
        };
        usersLabel = BuildMetricLabel("Total users: 0", new Point(24, 120));
        sessionsLabel = BuildMetricLabel("Sessions analyzed: 0", new Point(24, 150));
        averageViewsLabel = BuildMetricLabel("Avg views per artifact: 0", new Point(24, 180));

        analyticsList = new ListView
        {
            Location = new Point(24, 230),
            Width = 640,
            Height = 560,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            Font = new Font("Segoe UI", 9f)
        };
        analyticsList.Columns.Add("Artifact", 210);
        analyticsList.Columns.Add("Views", 70);
        analyticsList.Columns.Add("Interactions", 90);
        analyticsList.Columns.Add("Positivity", 90);
        analyticsList.Columns.Add("Duration", 80);
        analyticsList.Columns.Add("Final Score", 90);

        rightPanel.Controls.Add(favorableArtifactLabel);
        rightPanel.Controls.Add(scoreReasonLabel);
        rightPanel.Controls.Add(usersLabel);
        rightPanel.Controls.Add(sessionsLabel);
        rightPanel.Controls.Add(averageViewsLabel);
        rightPanel.Controls.Add(analyticsList);

        adminHintLabel = new Label
        {
            Text = "Gesture controls: Swipe Left/Right = select artifact, Circle = edit, Mute = delete, DarkMode = create",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(88, 98, 112),
            Location = new Point(24, 800)
        };
        rightPanel.Controls.Add(adminHintLabel);

        ReloadAll();
    }

    private static Label BuildMetricLabel(string text, Point point)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            AutoSize = true,
            Location = point,
            ForeColor = Color.FromArgb(32, 52, 75)
        };
    }

    private static Button BuildActionButton(string text, Point location, Color backColor)
    {
        return new Button
        {
            Text = text,
            Width = 124,
            Height = 30,
            Location = location,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.8f, FontStyle.Bold)
        };
    }

    private void ReloadAll()
    {
        artifacts = LoadArtifacts();
        RefreshCategoryFilter();
        RefreshGrid();
        RefreshAnalytics();
    }

    private List<AdminArtifact> LoadArtifacts()
    {
        if (string.IsNullOrWhiteSpace(artifactsPath) || !File.Exists(artifactsPath))
        {
            return new List<AdminArtifact>();
        }

        try
        {
            var serializer = new JavaScriptSerializer();
            var json = File.ReadAllText(artifactsPath);
            var root = serializer.Deserialize<AdminArtifactRoot>(json);
            if (root == null || root.artifacts == null)
            {
                return new List<AdminArtifact>();
            }

            foreach (var artifact in root.artifacts)
            {
                if (artifact == null) continue;
                if (string.IsNullOrWhiteSpace(artifact.category)) artifact.category = !string.IsNullOrWhiteSpace(artifact.country) ? artifact.country : artifact.era;
                if (string.IsNullOrWhiteSpace(artifact.period)) artifact.period = artifact.birthDate;
                if (string.IsNullOrWhiteSpace(artifact.historicalInfo)) artifact.historicalInfo = artifact.narration;
            }

            return root.artifacts.OrderBy(a => a.id).ToList();
        }
        catch
        {
            return new List<AdminArtifact>();
        }
    }

    private void SaveArtifacts()
    {
        var root = new AdminArtifactRoot { artifacts = artifacts.OrderBy(a => a.id).ToList() };
        var serializer = new JavaScriptSerializer();
        var json = serializer.Serialize(root).Replace("\"artifacts\":[", "\"artifacts\": [");
        File.WriteAllText(artifactsPath, json);
        onArtifactsChanged?.Invoke();
    }

    private void RefreshCategoryFilter()
    {
        var selected = categoryFilter.SelectedItem as string;
        var categories = artifacts.Select(a => string.IsNullOrWhiteSpace(a.category) ? "Uncategorized" : a.category.Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();

        categoryFilter.Items.Clear();
        categoryFilter.Items.Add("All Categories");
        foreach (var category in categories) categoryFilter.Items.Add(category);
        categoryFilter.SelectedItem = categories.Contains(selected) ? selected : "All Categories";
    }

    private void RefreshGrid()
    {
        var query = (searchBox.Text ?? string.Empty).Trim().ToLowerInvariant();
        if (query == "search title, category, tags, era...") query = string.Empty;
        var category = (categoryFilter.SelectedItem as string) ?? "All Categories";

        var filtered = artifacts.Where(a =>
        {
            var categoryValue = string.IsNullOrWhiteSpace(a.category) ? "Uncategorized" : a.category;
            bool categoryMatch = category == "All Categories" || string.Equals(categoryValue, category, StringComparison.OrdinalIgnoreCase);
            if (!categoryMatch) return false;
            if (string.IsNullOrWhiteSpace(query)) return true;

            var haystack = string.Join(" ", new[] { a.name, a.description, a.category, a.tags, a.period, a.era, a.origin, a.country }).ToLowerInvariant();
            return haystack.Contains(query);
        }).ToList();

        artifactGrid.DataSource = filtered;
        SelectFirstArtifactIfNeeded();
    }

    private void SelectFirstArtifactIfNeeded()
    {
        if (artifactGrid.Rows.Count == 0)
        {
            return;
        }

        if (artifactGrid.CurrentCell == null)
        {
            artifactGrid.Rows[0].Selected = true;
            artifactGrid.CurrentCell = artifactGrid.Rows[0].Cells[0];
        }
    }

    private AdminArtifact GetSelectedArtifact()
    {
        if (artifactGrid.SelectedRows.Count == 0) return null;
        return artifactGrid.SelectedRows[0].DataBoundItem as AdminArtifact;
    }

    private void CreateArtifact()
    {
        var browser = new AdminTemplateBrowserForm();
        activeTemplateBrowser = browser;
        if (browser.ShowDialog(this) != DialogResult.OK || browser.ResultArtifact == null)
        {
            activeTemplateBrowser = null;
            browser.Dispose();
            return;
        }
        activeTemplateBrowser = null;
        browser.Dispose();

        var templateArtifact = browser.ResultArtifact;
        int nextId = artifacts.Count == 0 ? 0 : artifacts.Max(a => a.id) + 1;
        int nextTuioId = artifacts.Count == 0 ? 0 : artifacts.Max(a => a.tuioId) + 1;
        templateArtifact.id = nextId;
        templateArtifact.tuioId = nextTuioId;

        if (artifacts.Any(a => a.id == templateArtifact.id)) { MessageBox.Show(this, "Artifact ID conflict.", "Create Artifact", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (artifacts.Any(a => a.tuioId == templateArtifact.tuioId)) { MessageBox.Show(this, "TUIO ID conflict.", "Create Artifact", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        artifacts.Add(templateArtifact);
        SaveArtifacts();
        ReloadAll();
    }

    private void EditSelectedArtifact()
    {
        var selected = GetSelectedArtifact();
        if (selected == null) { MessageBox.Show(this, "Select an artifact to edit.", "Edit Artifact", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        var workingCopy = new AdminArtifact
        {
            id = selected.id,
            tuioId = selected.tuioId,
            name = selected.name,
            birthDate = selected.birthDate,
            era = selected.era,
            origin = selected.origin,
            description = selected.description,
            narration = selected.narration,
            objPath = selected.objPath,
            audioPath = selected.audioPath,
            color = selected.color,
            country = selected.country,
            category = selected.category,
            tags = selected.tags,
            historicalInfo = selected.historicalInfo,
            period = selected.period
        };

        using (var form = new AdminArtifactEditorForm(workingCopy))
        {
            if (form.ShowDialog() != DialogResult.OK || form.Artifact == null) return;
            if (artifacts.Any(a => a != selected && a.id == form.Artifact.id)) { MessageBox.Show(this, "Artifact ID already exists.", "Edit Artifact", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (artifacts.Any(a => a != selected && a.tuioId == form.Artifact.tuioId)) { MessageBox.Show(this, "TUIO ID already exists.", "Edit Artifact", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            selected.id = form.Artifact.id;
            selected.tuioId = form.Artifact.tuioId;
            selected.name = form.Artifact.name;
            selected.birthDate = form.Artifact.birthDate;
            selected.era = form.Artifact.era;
            selected.origin = form.Artifact.origin;
            selected.description = form.Artifact.description;
            selected.narration = form.Artifact.narration;
            selected.objPath = form.Artifact.objPath;
            selected.audioPath = form.Artifact.audioPath;
            selected.color = form.Artifact.color;
            selected.country = form.Artifact.country;
            selected.category = form.Artifact.category;
            selected.tags = form.Artifact.tags;
            selected.historicalInfo = form.Artifact.historicalInfo;
            selected.period = form.Artifact.period;

            SaveArtifacts();
            ReloadAll();
        }
    }

    private void DeleteSelectedArtifact()
    {
        var selected = GetSelectedArtifact();
        if (selected == null) { MessageBox.Show(this, "Select an artifact to delete.", "Delete Artifact", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        var result = MessageBox.Show(this, "Delete artifact '" + selected.name + "'? This action cannot be undone.", "Confirm Deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes) return;

        artifacts.Remove(selected);
        SaveArtifacts();
        ReloadAll();
    }

    private void MoveSelection(int delta)
    {
        if (artifactGrid.Rows.Count == 0)
        {
            return;
        }

        int currentIndex = artifactGrid.CurrentCell != null ? artifactGrid.CurrentCell.RowIndex : 0;
        int nextIndex = (currentIndex + delta) % artifactGrid.Rows.Count;
        if (nextIndex < 0)
        {
            nextIndex += artifactGrid.Rows.Count;
        }

        artifactGrid.ClearSelection();
        artifactGrid.Rows[nextIndex].Selected = true;
        artifactGrid.CurrentCell = artifactGrid.Rows[nextIndex].Cells[0];
        artifactGrid.FirstDisplayedScrollingRowIndex = nextIndex;
    }

    public bool HandleGestureCommand(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return false;
        }

        // If the template browser is active, delegate all gestures to it
        // and consume them so nothing leaks to the dashboard underneath.
        if (activeTemplateBrowser != null && !activeTemplateBrowser.IsDisposed)
        {
            activeTemplateBrowser.HandleGestureCommand(gesture);
            return true;
        }

        if (gl == "adminnextartifact" || gl == "adminnext" || gl == "nextartifact")
        {
            MoveSelection(1);
            return true;
        }

        if (gl == "adminprevartifact" || gl == "adminprev" || gl == "prevartifact")
        {
            MoveSelection(-1);
            return true;
        }

        if (gl == "admineditartifact" || gl == "adminedit" || gl == "editartifact")
        {
            EditSelectedArtifact();
            return true;
        }

        if (gl == "admindeleteartifact" || gl == "admindelete" || gl == "delete" || gl == "deleteartifact")
        {
            DeleteSelectedArtifact();
            return true;
        }

        if (gl == "admincreateartifact" || gl == "admincreate" || gl == "createartifact")
        {
            CreateArtifact();
            return true;
        }

        if (gl == "swiperight")
        {
            MoveSelection(1);
            return true;
        }

        if (gl == "swipeleft")
        {
            MoveSelection(-1);
            return true;
        }

        if (gl == "circle")
        {
            EditSelectedArtifact();
            return true;
        }

        if (gl == "mute")
        {
            DeleteSelectedArtifact();
            return true;
        }

        if (gl == "darkmode")
        {
            CreateArtifact();
            return true;
        }

        return false;
    }

    private void RefreshAnalytics()
    {
        var analytics = BuildAnalytics();
        analyticsList.Items.Clear();

        foreach (var item in analytics)
        {
            var row = new ListViewItem(item.Name);
            row.SubItems.Add(item.Views.ToString());
            row.SubItems.Add(item.Interactions.ToString());
            row.SubItems.Add(item.PositiveEmotionScore.ToString("0.00"));
            row.SubItems.Add(item.DurationScore.ToString("0.00"));
            row.SubItems.Add(item.FinalScore.ToString("0.000"));
            analyticsList.Items.Add(row);
        }

        if (analytics.Count > 0)
        {
            var top = analytics[0];
            favorableArtifactLabel.Text = "Most Favorable Artifact: " + top.Name;
            scoreReasonLabel.Text = "Selected because it leads with strong composite engagement: positivity=" + top.PositiveEmotionScore.ToString("0.00") + ", views=" + top.Views + ", duration score=" + top.DurationScore.ToString("0.00") + ", interactions=" + top.Interactions + ", final=" + top.FinalScore.ToString("0.000") + ".";
        }
        else
        {
            favorableArtifactLabel.Text = "Most Favorable Artifact: -";
            scoreReasonLabel.Text = "No artifact analytics data has been captured yet.";
        }
    }

    private List<ArtifactAnalytics> BuildAnalytics()
    {
        var analyticsMap = new Dictionary<string, ArtifactAnalytics>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in artifacts)
        {
            var name = string.IsNullOrWhiteSpace(artifact.name) ? "Artifact " + artifact.id : artifact.name.Trim();
            if (!analyticsMap.ContainsKey(name)) analyticsMap[name] = new ArtifactAnalytics { Name = name };
        }

        int totalUsers = 0;
        if (File.Exists(contextPath))
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var root = serializer.DeserializeObject(File.ReadAllText(contextPath)) as Dictionary<string, object>;
                if (root != null && root.ContainsKey("users"))
                {
                    var users = root["users"] as Dictionary<string, object>;
                    if (users != null)
                    {
                        totalUsers = users.Count;
                        foreach (var userEntry in users)
                        {
                            var userData = userEntry.Value as Dictionary<string, object>;
                            if (userData == null) continue;

                            if (userData.ContainsKey("opened_artifacts"))
                            {
                                var opened = userData["opened_artifacts"] as Dictionary<string, object>;
                                if (opened != null)
                                {
                                    foreach (var openedEntry in opened)
                                    {
                                        var name = MatchArtifactName(openedEntry.Key);
                                        if (name == null) continue;
                                        analyticsMap[name].Views += 1;
                                        analyticsMap[name].Interactions += 1;
                                    }
                                }
                            }

                            if (userData.ContainsKey("artifact_scores"))
                            {
                                var scores = userData["artifact_scores"] as Dictionary<string, object>;
                                if (scores != null)
                                {
                                    foreach (var scoreEntry in scores)
                                    {
                                        var name = MatchArtifactName(scoreEntry.Key);
                                        if (name == null) continue;
                                        double value;
                                        if (double.TryParse(Convert.ToString(scoreEntry.Value), out value))
                                        {
                                            analyticsMap[name].DurationScore += Math.Max(0.0, value);
                                            analyticsMap[name].Interactions += Math.Abs(value) >= 0.5 ? 1 : 0;
                                        }
                                    }
                                }
                            }

                            if (userData.ContainsKey("context"))
                            {
                                var context = userData["context"] as Dictionary<string, object>;
                                if (context != null)
                                {
                                    string artifactName = Convert.ToString(context.ContainsKey("current_artifact") ? context["current_artifact"] : "");
                                    string emotion = Convert.ToString(context.ContainsKey("last_emotion") ? context["last_emotion"] : "neutral").Trim().ToLowerInvariant();
                                    var name = MatchArtifactName(artifactName);
                                    if (name != null) analyticsMap[name].PositiveEmotionScore += EmotionWeight(emotion);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        int sessionsCount = 0;
        if (!string.IsNullOrWhiteSpace(reportsPath) && Directory.Exists(reportsPath))
        {
            try
            {
                foreach (var summaryPath in Directory.GetFiles(reportsPath, "session_summary.json", SearchOption.AllDirectories))
                {
                    sessionsCount += 1;
                    try
                    {
                        var serializer = new JavaScriptSerializer();
                        var root = serializer.DeserializeObject(File.ReadAllText(summaryPath)) as Dictionary<string, object>;
                        if (root == null || !root.ContainsKey("reports")) continue;
                        var reports = root["reports"] as Dictionary<string, object>;
                        if (reports == null || !reports.ContainsKey("artifacts")) continue;
                        var artifactsReport = reports["artifacts"] as Dictionary<string, object>;
                        if (artifactsReport == null || !artifactsReport.ContainsKey("summary")) continue;
                        var summary = artifactsReport["summary"] as Dictionary<string, object>;
                        if (summary == null || !summary.ContainsKey("ranked_artifacts")) continue;

                        var rankedList = summary["ranked_artifacts"] as ArrayList;
                        if (rankedList == null) continue;

                        foreach (var itemObj in rankedList)
                        {
                            var item = itemObj as Dictionary<string, object>;
                            if (item == null) continue;

                            var name = MatchArtifactName(Convert.ToString(item.ContainsKey("name") ? item["name"] : ""));
                            if (name == null) continue;

                            double score;
                            if (double.TryParse(Convert.ToString(item.ContainsKey("score") ? item["score"] : 0.0), out score))
                            {
                                analyticsMap[name].DurationScore += Math.Max(0.0, score);
                            }

                            bool opened;
                            if (bool.TryParse(Convert.ToString(item.ContainsKey("opened") ? item["opened"] : "false"), out opened) && opened)
                            {
                                analyticsMap[name].Views += 1;
                                analyticsMap[name].Interactions += 1;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        usersLabel.Text = "Total users: " + totalUsers;
        sessionsLabel.Text = "Sessions analyzed: " + sessionsCount;

        var analytics = analyticsMap.Values.ToList();
        NormalizeAndScore(analytics);

        double avgViews = analytics.Count == 0 ? 0.0 : analytics.Average(a => a.Views);
        averageViewsLabel.Text = "Avg views per artifact: " + avgViews.ToString("0.00");

        return analytics.OrderByDescending(a => a.FinalScore).ThenByDescending(a => a.Views).ThenByDescending(a => a.Interactions).ToList();
    }

    private string MatchArtifactName(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;

        var clean = candidate.Trim();
        var direct = artifacts.FirstOrDefault(a => string.Equals(a.name, clean, StringComparison.OrdinalIgnoreCase));
        if (direct != null) return direct.name;

        var normalized = clean.ToLowerInvariant();
        foreach (var artifact in artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.name)) continue;
            if (artifact.name.Trim().ToLowerInvariant() == normalized) return artifact.name;
        }

        return null;
    }

    private static double EmotionWeight(string emotion)
    {
        switch ((emotion ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "happy": return 1.0;
            case "surprised": return 0.75;
            case "neutral": return 0.35;
            case "sad": return -0.4;
            default: return 0.1;
        }
    }

    private static void NormalizeAndScore(List<ArtifactAnalytics> analytics)
    {
        if (analytics == null || analytics.Count == 0) return;

        double minPos = analytics.Min(a => a.PositiveEmotionScore);
        double maxPos = analytics.Max(a => a.PositiveEmotionScore);
        double minViews = analytics.Min(a => (double)a.Views);
        double maxViews = analytics.Max(a => (double)a.Views);
        double minDur = analytics.Min(a => a.DurationScore);
        double maxDur = analytics.Max(a => a.DurationScore);
        double minInter = analytics.Min(a => (double)a.Interactions);
        double maxInter = analytics.Max(a => (double)a.Interactions);

        foreach (var item in analytics)
        {
            double posNorm = Normalize(item.PositiveEmotionScore, minPos, maxPos);
            double viewsNorm = Normalize(item.Views, minViews, maxViews);
            double durNorm = Normalize(item.DurationScore, minDur, maxDur);
            double interNorm = Normalize(item.Interactions, minInter, maxInter);

            item.FinalScore = (0.35 * posNorm) + (0.30 * viewsNorm) + (0.25 * durNorm) + (0.10 * interNorm);
        }
    }

    private static double Normalize(double value, double min, double max)
    {
        if (Math.Abs(max - min) < 0.000001) return 0.5;
        return (value - min) / (max - min);
    }
}
