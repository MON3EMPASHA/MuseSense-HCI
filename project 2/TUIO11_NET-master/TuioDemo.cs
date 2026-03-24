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
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

public class TuioDemo : Form , TuioListener
	{
        int slideIndex = 0;
        string[] slideImages ;
        SolidBrush cardBsh = new SolidBrush(Color.FromArgb(30, 30, 60)); 
        string uname = "Visitor";
        Image upic = null;
        
        // setting default colors and different gender colors
        private struct ColorTheme
        {
            public Color backgroundDark;
            public Color cardBackground;
            public Color accentLight; 
            public Color accentBubble;
            public Color avatarBackground;
        }
        
        private ColorTheme maleTheme = new ColorTheme
        {
            backgroundDark = Color.FromArgb(0, 0, 64),
            cardBackground = Color.FromArgb(30, 30, 60),
            accentLight = Color.FromArgb(100, 200, 255),
            accentBubble = Color.FromArgb(64, 64, 64),
            avatarBackground = Color.FromArgb(80, 80, 120)
        };
        
        private ColorTheme femaleTheme = new ColorTheme
        {
            backgroundDark = Color.FromArgb(64, 20, 40),
            cardBackground = Color.FromArgb(80, 30, 60),
            accentLight = Color.FromArgb(255, 150, 200),
            accentBubble = Color.FromArgb(100, 40, 80),
            avatarBackground = Color.FromArgb(120, 60, 90)
        };
        
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
		SolidBrush fntBrush = new SolidBrush(Color.White);
		SolidBrush bgrBrush = new SolidBrush(Color.FromArgb(0,0,64));       // changes by gender
		SolidBrush cardBsh_dynamic = new SolidBrush(Color.FromArgb(30, 30, 60));
		SolidBrush accentBrush = new SolidBrush(Color.FromArgb(100, 200, 255));
		SolidBrush avatarBrush = new SolidBrush(Color.FromArgb(80, 80, 120));
		SolidBrush curBrush = new SolidBrush(Color.FromArgb(192, 0, 192));
		SolidBrush objBrush = new SolidBrush(Color.FromArgb(64, 0, 0));
		SolidBrush blbBrush = new SolidBrush(Color.FromArgb(64, 64, 64));
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
        }

        class UserRoot
        {
                public List<UserRecord> artifacts { get; set; } // Keep the same property name for JSON compatibility
        }

        List<ArtifactRecord> artifacts = new List<ArtifactRecord>();
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
        int selectedMenuItem = -1; // -1=none, 0=Egypt, 1=China, 2=Europe, 3=Favorites
        long tuioMarker100SessionId = -1;

		public TuioDemo(int port) {
        System.Timers.Timer slideTimer = new System.Timers.Timer(3000);
        slideTimer.Elapsed += (s, e) => { slideIndex = (slideIndex + 1) % 5; ; Invoke((Action)Invalidate); };
        slideTimer.Start();
        verbose = false;
			fullscreen = false;
			width = window_width;
			height = window_height;


			this.ClientSize = new System.Drawing.Size(width, height);
			this.Name = "TuioDemo";
			this.Text = "TuioDemo";
        this.WindowState = FormWindowState.Maximized;
        this.FormBorderStyle = FormBorderStyle.None;

        this.Closing+=new CancelEventHandler(Form_Closing);
			this.KeyDown+=new KeyEventHandler(Form_KeyDown);

			this.SetStyle( ControlStyles.AllPaintingInWmPaint |
							ControlStyles.UserPaint |
							ControlStyles.DoubleBuffer, true);

			objectList = new Dictionary<long,TuioObject>(128);
			cursorList = new Dictionary<long,TuioCursor>(128);
			blobList   = new Dictionary<long,TuioBlob>(128);
			
			client = new TuioClient(port);
			client.addTuioListener(this);

			client.connect();
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
                tuioMarker100Visible = true;
                tuioMarker100SessionId = o.SessionID;
                UpdateMenuSelectionFromRotation(o.Angle);
                Invalidate();
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
                tuioMarker100Visible = true;
                tuioMarker100SessionId = o.SessionID;
                UpdateMenuSelectionFromRotation(o.Angle);
                Invalidate();
            }
            else
            {
                NavigateToArtifactByMarker(o.SymbolID);
            }
		}

		public void removeTuioObject(TuioObject o) {
			lock(objectList) {
				objectList.Remove(o.SessionID);
			}
			if (verbose) Console.WriteLine("del obj "+o.SymbolID+" ("+o.SessionID+")");
            
            // Handle circular menu marker removal (TUIO ID 100)
            if (o.SymbolID == 100 && o.SessionID == tuioMarker100SessionId)
            {
                tuioMarker100Visible = false;
                
                // Navigate to the selected menu item
                if (selectedMenuItem >= 0 && selectedMenuItem < 3)
                {
                    currentCountry = selectedMenuItem; // 0=Egypt, 1=China, 2=Europe
                }
                else if (selectedMenuItem == 3)
                {
                    page = 6; // Go to Favorites page
                }
                
                selectedMenuItem = -1;
                tuioMarker100SessionId = -1;
                Invalidate();
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

        public bool connectToSocket(string host, int portNumber)
        {
            try
            {
                client = new TcpClient(host, portNumber);
                stream = client.GetStream();
                reader = new StreamReader(stream, Encoding.UTF8);
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

    }
    string msg = "";
	string oldmsg = "";
    int login = 0;
    int page = 0;    
    int currentCountry = 0;  // 0=Egypt, 1=China, 2=Europe
    string[] countries = { "Egypt", "China", "Europe" };    
    string btStatus = "Waiting...";

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
            SaveUserFavorites();
            return true;
        }

        return false;
    }

    // remove artifact from user's favorites
    void RemoveArtifactFromFavorites(int artifactId)
    {
        if (currentUser == null || currentUser.favorites == null) return;
        
        currentUser.favorites.Remove(artifactId);
        SaveUserFavorites();
    }

    // save user favorites back to users.json
    void SaveUserFavorites()
    {
        if (string.IsNullOrWhiteSpace(usersJsonPath) || currentUser == null) return;

        try
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(allUsers);
            File.WriteAllText(usersJsonPath, json);
            Console.WriteLine("Saved user favorites for: " + currentUser.name);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to save user favorites: " + ex.Message);
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

    // get artifacts by country name
    List<ArtifactRecord> GetArtifactsByCountry(string country)
    {
        List<ArtifactRecord> result = new List<ArtifactRecord>();
        foreach (ArtifactRecord artifact in artifacts)
        {
            if (artifact.country == country) result.Add(artifact);
        }
        return result;
    }

    // gui color is blue if gender is male pink if female
    private void SetThemeByGender(string gender)
    {
        ColorTheme selectedTheme = maleTheme;
        
        if (!string.IsNullOrEmpty(gender) && gender.ToLower() == "female")
        {
            selectedTheme = femaleTheme;
        }
        
        bgrBrush.Color = selectedTheme.backgroundDark;
        cardBsh.Color = selectedTheme.cardBackground;
        accentBrush.Color = selectedTheme.accentLight;
        avatarBrush.Color = selectedTheme.avatarBackground;
        blbBrush.Color = selectedTheme.accentBubble;
        
        Console.WriteLine($"Theme applied: {gender ?? "male"} ({(gender?.ToLower() == "female" ? "PINK" : "BLUE")})");
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
        // convert the radians to degrees formula 
        double angleDegrees = angleRadians * 180.0 / Math.PI;
        angleDegrees = angleDegrees % 360.0;
        if (angleDegrees < 0) angleDegrees += 360.0;

        
        // countries degresse index
        int[] menuPositions = { 0, 90, 180, 270 }; // China, Europe, Favorites, Egypt
        double minDifference = 360.0;
        int closestMenuItem = -1;

        for (int i = 0; i < 4; i++)
        {
            
            double diff = Math.Abs(angleDegrees - menuPositions[i]);
            if (diff > 180) diff = 360 - diff;

            if (diff < minDifference)
            {
                minDifference = diff;
                closestMenuItem = i;
            }
        }

        int[] itemMap = { 1, 2, 3, 0 }; 
        selectedMenuItem = itemMap[closestMenuItem];
        
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
                Invalidate();
            });
            return;
        }

        selectedArtifactId = artifact.id;
        artifactFavoriteHint = "Make a CIRCLE to add to favorites!";
        page = 5;
        Invalidate();
    }


    class LoginPayload
    {
        public string type { get; set; }
        public string name { get; set; }
        public string age { get; set; }
        public string gender { get; set; }
        public string mac { get; set; }
        public string Profile { get; set; }
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
            btStatus = "Matched";
            
            // change theme by gender
            if (currentUser != null)
            {
                SetThemeByGender(currentUser.gender);
            }
            else if (!string.IsNullOrEmpty(payload.gender))
            {
                SetThemeByGender(payload.gender);
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
            return;
        }
        
        while (true)
        {
            msg = c.recieveMessage();
            if (string.IsNullOrWhiteSpace(msg))
            {
                btStatus = "Waiting...";
                Invoke((Action)(Invalidate));
                continue;
            }
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
                    
                    // load different theme based on context
                    if (currentUser != null)
                    {
                        SetThemeByGender(currentUser.gender);
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
                if (msg.Trim() == "SwipeRight")
                {
                    if (page == 6)
                        page = 0;
                    else
                    {
                        currentCountry++;
                        if (currentCountry > 2) currentCountry = 0;
                    }
                }
                if (msg.Trim() == "SwipeLeft")
                {
                    if (page != 6)
                    {
                        currentCountry--;
                        if (currentCountry < 0) currentCountry = 2;
                    }
                }
                if (msg.Trim() == "Circle" && page == 5 && selectedArtifactId >= 0)
                {
                    if (AddArtifactToFavorites(selectedArtifactId))
                    {
                        artifactFavoriteHint = "Artifact added to favourites";
                        Console.WriteLine("Artifact added to favourites");
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
			// Getting the graphics object
			Graphics g = pevent.Graphics;
        g.FillRectangle(bgrBrush, new Rectangle(0, 0, this.ClientSize.Width, this.ClientSize.Height));
        //gui for logn fr:

        if (uname == "Visitor" && page != 5)
        {
            int cw = 500, ch = 500;
            int cX = (this.ClientSize.Width - cw) / 2;
            int cY = (this.ClientSize.Height - ch) / 2;
           
            g.FillRectangle(cardBsh, cX, cY, cw, ch);
            g.FillEllipse(avatarBrush, cX + 190, cY + 60, 120, 120);
            Font hellofont = new Font("Arial", 22f, FontStyle.Bold);
            if (upic != null)
                g.DrawImage(upic, cX + 190, cY + 60, 120, 120);
            else
                g.FillEllipse(avatarBrush, cX + 190, cY + 60, 120, 120);
            g.DrawString("Hello, " + uname, hellofont, fntBrush, cX + 150, cY + 210);
            Font otherfont = new Font("Arial", 13f);
            SolidBrush silverboibush = new SolidBrush(Color.Silver);
            g.DrawString("Bluetooth Verification", otherfont, silverboibush, cX + 140, cY + 270);
            Font statusfont = new Font("Arial", 11f, FontStyle.Italic);
            g.DrawString(btStatus, statusfont, accentBrush, cX + 120, cY + 330);
           
            //end of gui for login
        }
        // After login: show country artifacts grid
        else if (uname != "Visitor" && page != 5)
        {
            // Get current country name and its artifacts
            string selectedCountry = countries[currentCountry];
            List<ArtifactRecord> countryArtifacts = GetArtifactsByCountry(selectedCountry);

            // Draw header with country name and swipe hints
            g.DrawString("Hello, " + uname, new Font("Arial", 20f, FontStyle.Bold), fntBrush, 40, 30);
            if (upic != null)
                g.DrawImage(upic, 60, 90, 100, 100);
            else
                g.FillEllipse(avatarBrush, 60, 90, 100, 100);

            g.DrawString(selectedCountry, new Font("Arial", 28f, FontStyle.Bold), fntBrush, this.ClientSize.Width / 2 - 60, 50);
            g.DrawString("Swipe Left/Right to change country  |  Scan marker to view artifact", new Font("Arial", 11f, FontStyle.Italic), new SolidBrush(Color.Silver), 40, this.ClientSize.Height - 40);

            // Draw artifact grid (show up to 6 artifacts: 3 columns x 2 rows)
            if (countryArtifacts.Count > 0)
            {
                int cardW = 280;
                int cardH = 280;
                int gap = 30;
                int colsPerRow = 3;
                int totalWidth = (cardW + gap) * colsPerRow;
                int startX = (this.ClientSize.Width - totalWidth) / 2;
                int startY = 130;

                for (int i = 0; i < countryArtifacts.Count && i < 6; i++)
                {
                    ArtifactRecord artifact = countryArtifacts[i];
                    int col = i % colsPerRow;
                    int row = i / colsPerRow;
                    int x = startX + col * (cardW + gap);
                    int y = startY + row * (cardH + gap + 50);

                    // Draw card background
                    g.FillRectangle(cardBsh, x, y, cardW, cardH);

                    // Draw artifact image
                    string imagePath = ResolveArtifactAssetPath(artifact.objPath);
                    if (File.Exists(imagePath))
                    {
                        try
                        {
                            Image artifactImg = Image.FromFile(imagePath);
                            g.DrawImage(artifactImg, x + 10, y + 10, cardW - 20, cardH - 60);
                            artifactImg.Dispose();
                        }
                        catch { }
                    }

                    // Draw artifact name below image
                    g.DrawString(artifact.name, new Font("Arial", 11f, FontStyle.Bold), fntBrush, x + 10, y + cardH - 45);

                    // Draw TUIO marker ID
                    g.DrawString("TUIO: " + artifact.tuioId, new Font("Arial", 10f), new SolidBrush(Color.Yellow), x + 10, y + cardH - 25);
                }
            }
        }
        // artifact details page (opened directly by marker scan)
        else if (page == 5 && selectedArtifactId >= 0)
        {
            ArtifactRecord artifact = GetArtifactById(selectedArtifactId);
            if (artifact != null)
            {
                int cardW = 1200;
                int cardH = 640;
                int cardX = (this.ClientSize.Width - cardW) / 2;
                int cardY = (this.ClientSize.Height - cardH) / 2;
                g.FillRectangle(cardBsh, cardX, cardY, cardW, cardH);

                int imgX = cardX + 30;
                int imgY = cardY + 70;
                int imgW = 430;
                int imgH = 500;
                g.FillRectangle(new SolidBrush(Color.FromArgb(60, 60, 100)), imgX, imgY, imgW, imgH);

                string imagePath = ResolveArtifactAssetPath(artifact.objPath);
                if (File.Exists(imagePath))
                {
                    Image artifactImage = Image.FromFile(imagePath);
                    g.DrawImage(artifactImage, imgX + 10, imgY + 10, imgW - 20, imgH - 20);
                    artifactImage.Dispose();
                }

                int textX = imgX + imgW + 30;
                int textY = imgY;
                int textW = cardW - (textX - cardX) - 30;

                g.DrawString(artifact.name, new Font("Arial", 24f, FontStyle.Bold), fntBrush, textX, textY);
                g.DrawString("Marker ID (TUIO): " + artifact.tuioId, new Font("Arial", 12f, FontStyle.Italic), new SolidBrush(Color.LightGray), textX, textY + 44);

                Font keyFont = new Font("Arial", 12f, FontStyle.Bold);
                Font valFont = new Font("Arial", 12f);
                int lineY = textY + 80;

                g.DrawString("Birth Date:", keyFont, fntBrush, textX, lineY);
                g.DrawString(artifact.birthDate, valFont, fntBrush, textX + 120, lineY);
                lineY += 30;

                g.DrawString("Era:", keyFont, fntBrush, textX, lineY);
                g.DrawString(artifact.era, valFont, fntBrush, textX + 120, lineY);
                lineY += 30;

                g.DrawString("Origin:", keyFont, fntBrush, textX, lineY);
                g.DrawString(artifact.origin, valFont, fntBrush, textX + 120, lineY);
                lineY += 42;

                g.DrawString("Description:", keyFont, fntBrush, textX, lineY);
                RectangleF descRect = new RectangleF(textX, lineY + 26, textW, 280);
                g.DrawString(artifact.description, valFont, fntBrush, descRect);

                // Draw heart icon and favorite instructions
                g.DrawString(artifactFavoriteHint, new Font("Arial", 12f, FontStyle.Bold), new SolidBrush(Color.FromArgb(255, 192, 203)), textX, cardY + cardH - 60);
                g.DrawString("SwipeRight: Home  |  SwipeLeft: News", new Font("Arial", 11f, FontStyle.Italic), new SolidBrush(Color.Silver), textX, cardY + cardH - 34);
            }
        }
        // favorites page
        else if (page == 6)
        {
            g.DrawString("My Favorites", new Font("Arial", 28f, FontStyle.Bold), fntBrush, 50, 30);
            if (upic != null)
                g.DrawImage(upic, 60, 90, 100, 100);
            else
                g.FillEllipse(avatarBrush, 60, 90, 100, 100);
            g.DrawString("Hello, " + uname, new Font("Arial", 20f, FontStyle.Bold), fntBrush, 40, 30);

            if (currentUser == null || currentUser.favorites == null || currentUser.favorites.Count == 0)
            {
                g.DrawString("No favorites yet", new Font("Arial", 16f), fntBrush, 50, 150);
            }
            else
            {
                int w = 280, h = 280, gap = 30;
                int cols = 3;
                int totalWidth = (w + gap) * cols;
                int startX = (this.ClientSize.Width - totalWidth) / 2;
                int startY = 150;

                for (int i = 0; i < currentUser.favorites.Count; i++)
                {
                    ArtifactRecord artifact = GetArtifactById(currentUser.favorites[i]);
                    if (artifact != null)
                    {
                        int col = i % cols;
                        int row = i / cols;
                        int x = startX + col * (w + gap);
                        int y = startY + row * (h + gap + 50);

                        g.FillRectangle(cardBsh, x, y, w, h);
                        string imgPath = ResolveArtifactAssetPath(artifact.objPath);
                        if (File.Exists(imgPath))
                        {
                            Image img = Image.FromFile(imgPath);
                            g.DrawImage(img, x + 10, y + 10, w - 20, h - 60);
                            img.Dispose();
                        }
                        g.DrawString(artifact.name, new Font("Arial", 11f, FontStyle.Bold), fntBrush, x + 10, y + h - 45);
                    }
                }
            }
            g.DrawString("SwipeRight: Back", new Font("Arial", 11f, FontStyle.Italic), new SolidBrush(Color.Silver), 50, this.ClientSize.Height - 34);
        }
        // draw the cursor path
        if (cursorList.Count > 0) {
 			 lock(cursorList) {
			 foreach (TuioCursor tcur in cursorList.Values) {
					List<TuioPoint> path = tcur.Path;
					TuioPoint current_point = path[0];

					for (int i = 0; i < path.Count; i++) {
						TuioPoint next_point = path[i];
						g.DrawLine(curPen, current_point.getScreenX(width), current_point.getScreenY(height), next_point.getScreenX(width), next_point.getScreenY(height));
						current_point = next_point;
					}
					g.FillEllipse(curBrush, current_point.getScreenX(width) - height / 100, current_point.getScreenY(height) - height / 100, height / 50, height / 50);
					g.DrawString(tcur.CursorID + "", font, fntBrush, new PointF(tcur.getScreenX(width) - 10, tcur.getScreenY(height) - 10));
				}
			}
		 }

			// draw the objects
            if (objectList.Count > 0 && page != 5) {
 				lock(objectList) {
					foreach (TuioObject tobj in objectList.Values) {
                    int size = height / 10;
                    int ox = tobj.getScreenX(width);
                    int oy = tobj.getScreenY(height);

                    g.TranslateTransform(ox, oy);
                    g.RotateTransform((float)(tobj.Angle / Math.PI * 180.0f));
                    g.TranslateTransform(-ox, -oy);

                    g.FillRectangle(objBrush, new Rectangle(ox - size / 2, oy - size / 2, size, size));







                    g.TranslateTransform(ox, oy);
						g.RotateTransform(-1 * (float)(tobj.Angle / Math.PI * 180.0f));
						g.TranslateTransform(-ox, -oy);

						g.DrawString(tobj.SymbolID + "", font, fntBrush, new PointF(ox - 10, oy - 10));
					}
				}
			}

			// draw the blobs
			if (blobList.Count > 0) {
				lock(blobList) {
					foreach (TuioBlob tblb in blobList.Values) {
						int bx = tblb.getScreenX(width);
						int by = tblb.getScreenY(height);
						float bw = tblb.Width*width;
						float bh = tblb.Height*height;

						g.TranslateTransform(bx, by);
						g.RotateTransform((float)(tblb.Angle / Math.PI * 180.0f));
						g.TranslateTransform(-bx, -by);

						g.FillEllipse(blbBrush, bx - bw / 2, by - bh / 2, bw, bh);

						g.TranslateTransform(bx, by);
						g.RotateTransform(-1 * (float)(tblb.Angle / Math.PI * 180.0f));
						g.TranslateTransform(-bx, -by);
						
						g.DrawString(tblb.BlobID + "", font, fntBrush, new PointF(bx, by));
					}
				}
			}

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
	 
    // Draw circular menu with 4 items (Egypt, China, Europe, Favorites)
    private void DrawCircularMenu(Graphics g, int screenWidth, int screenHeight)
    {
        if (!tuioMarker100Visible) return;

        // Menu configuration
        int centerX = screenWidth - this.ClientSize.Width / 2;
        int centerY = screenHeight - this.ClientSize.Height / 2;
        int radius = 90; 
        int itemSize = 80;
        int imageSize = 60;

        string[] menuLabels = { "Egypt", "China", "Europe", "Favorites" };
        string[] menuImages = { "Countries/egypt.png", "Countries/china.png", "Countries/europe.png", "heart.png" };
        
        double[] angles = { -90, 0, 90, 180 };

        for (int i = 0; i < 4; i++)
        {
            double radians = angles[i] * Math.PI / 180.0;
            int itemX = centerX + (int)(radius * Math.Cos(radians)) - itemSize / 2;
            int itemY = centerY + (int)(radius * Math.Sin(radians)) - itemSize / 2;

            bool isSelected = (i == selectedMenuItem);
            
            Color bgColor = isSelected 
                ? Color.FromArgb(255, 200, 0) // Yellow highlight for selected
                : Color.FromArgb(60, 60, 100); // Default dark blue
            SolidBrush itemBrush = new SolidBrush(bgColor);
            g.FillEllipse(itemBrush, itemX, itemY, itemSize, itemSize);

            // Draw border for selected item
            if (isSelected)
            {
                Pen highlightPen = new Pen(Color.White, 3);
                g.DrawEllipse(highlightPen, itemX, itemY, itemSize, itemSize);
            }

            // Load and draw the image
            string imagePath = menuImages[i];
            string absolutePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, imagePath);
            
            if (File.Exists(absolutePath))
            {
                try
                {
                    Image menuImage = Image.FromFile(absolutePath);
                    int imgX = itemX + (itemSize - imageSize) / 2;
                    int imgY = itemY + (itemSize - imageSize) / 2;
                    g.DrawImage(menuImage, imgX, imgY, imageSize, imageSize);
                    menuImage.Dispose();
                }
                catch { }
            }

            Font labelFont = new Font("Arial", 10f, FontStyle.Bold);
            SolidBrush labelBrush = isSelected ? new SolidBrush(Color.Yellow) : new SolidBrush(Color.White);
            StringFormat format = new StringFormat();
            format.Alignment = StringAlignment.Center;
            g.DrawString(menuLabels[i], labelFont, labelBrush, 
                         itemX + itemSize / 2, itemY + itemSize + 5, format);
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
