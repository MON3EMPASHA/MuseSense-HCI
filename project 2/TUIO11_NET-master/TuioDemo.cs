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
		SolidBrush bgrBrush = new SolidBrush(Color.FromArgb(0,0,64));
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
            NavigateToArtifactByMarker(o.SymbolID);
		}

		public void updateTuioObject(TuioObject o) {

			if (verbose) Console.WriteLine("set obj "+o.SymbolID+" "+o.SessionID+" "+o.X+" "+o.Y+" "+o.Angle+" "+o.MotionSpeed+" "+o.RotationSpeed+" "+o.MotionAccel+" "+o.RotationAccel);
            NavigateToArtifactByMarker(o.SymbolID);
		}

		public void removeTuioObject(TuioObject o) {
			lock(objectList) {
				objectList.Remove(o.SessionID);
			}
			if (verbose) Console.WriteLine("del obj "+o.SymbolID+" ("+o.SessionID+")");
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

        public bool connectToSocket(string host, int portNumber)
        {
            try
            {
                client = new TcpClient(host, portNumber);
                stream = client.GetStream();
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
                if (stream == null) return null;
                byte[] receiveBuffer = new byte[1024];
                int bytesReceived = stream.Read(receiveBuffer, 0, 1024);
                Console.WriteLine(bytesReceived);
                string data = Encoding.UTF8.GetString(receiveBuffer, 0, bytesReceived);
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
        string path = @"..\..\TUIO11_NET-master\bin\Debug\users.json";

        if (File.Exists(path))
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
                Console.WriteLine("Failed loading users from " + Path.GetFullPath(path) + ": " + ex.Message);
            }
        }

        Console.WriteLine("No valid users.json could be loaded.");
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

    // add artifact to user's favorites
    void AddArtifactToFavorites(int artifactId)
    {
        if (currentUser == null) return;
        
        if (currentUser.favorites == null)
            currentUser.favorites = new List<int>();
        
        if (!currentUser.favorites.Contains(artifactId))
        {
            currentUser.favorites.Add(artifactId);
            SaveUserFavorites();
        }
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
                page = 5;
                Invalidate();
            });
            return;
        }

        selectedArtifactId = artifact.id;
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
                    page++;
                    if (page > 6) page = 0;  // Include page 6 (favorites)
                    if (page == 4 && room == 0) page++; // skip egypt visitor eza enta fe room egypt
                    if (page == 2 && room == 1) page++; // skip china eza enta fe room china
                    if (page == 3 && room == 2) page++; // skip medieval eza enta fe medieval europe
                    
                }
                if (msg.Trim() == "SwipeLeft")
                {
                    page--;
                    if (page < 0) page = 6;  // Include page 6 (favorites)
                    if (page == 4 && room == 0) page--; // skip egypt visitor eza enta fe room egypt
                    if (page == 2 && room == 1) page--; // skip china eza enta fe room china
                    if (page == 3 && room == 2) page--; // skip medieval eza enta fe medieval europe
                }
                // Handle Circle gesture - add artifact to favorites
                if (msg.Trim() == "Circle" && page == 5 && selectedArtifactId >= 0)
                {
                    AddArtifactToFavorites(selectedArtifactId);
                    Console.WriteLine("Artifact " + selectedArtifactId + " added to favorites");
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
            SolidBrush avatarBrsh = new SolidBrush(Color.FromArgb(80, 80, 120));
            g.FillEllipse(avatarBrsh, cX + 190, cY + 60, 120, 120);
            Font hellofont = new Font("Arial", 22f, FontStyle.Bold);
            if (upic != null)
                g.DrawImage(upic, cX + 190, cY + 60, 120, 120);
            else
                g.FillEllipse(avatarBrsh, cX + 190, cY + 60, 120, 120);
            g.DrawString("Hello, " + uname, hellofont, fntBrush, cX + 150, cY + 210);
            Font otherfont = new Font("Arial", 13f);
            SolidBrush silverboibush = new SolidBrush(Color.Silver);
            g.DrawString("Bluetooth Verification", otherfont, silverboibush, cX + 140, cY + 270);
            Font statusfont = new Font("Arial", 11f, FontStyle.Italic);
            SolidBrush bluebsh = new SolidBrush(Color.FromArgb(100, 200, 255));
            g.DrawString(btStatus, statusfont, bluebsh, cX + 120, cY + 330);
           
            //end of gui for login
        }
        //gui for home page
        else if(page==0)
        {
          
            int cW = 400, cH = 350, gap = 30;
            int startX = (this.ClientSize.Width - (cW * 3 + gap * 2)) / 2;
            int startY = (this.ClientSize.Height - cH) / 2;

            string[] labels = { "Recommended", "Previously Seen", "Favorites" };
            string[] img_links = { "Untitled.jpg", "Untitled1.jpg", "12.jpg" };

            for (int i = 0; i < 3; i++)
            {
                int x = startX + i * (cW + gap);

              
                g.FillRectangle(cardBsh, x, startY, cW, cH);

         
                g.FillRectangle(new SolidBrush(Color.FromArgb(60, 60, 100)), x + 10, startY + 10, cW - 20, cH - 50);
                Image img = Image.FromFile(img_links[i]);
                g.DrawImage(img, x + 10, startY + 10, cW - 20, cH - 50);
         
                g.DrawString(labels[i], new Font("Arial", 13f, FontStyle.Bold), fntBrush,
                             x + 14, startY + cH - 34);
            }

            if (upic != null)
                g.DrawImage(upic, 60, 90, 100, 100);
            else
                g.FillEllipse(new SolidBrush(Color.FromArgb(80, 80, 120)), 60, 90, 100, 100);
            g.DrawString("Hello, " + uname, new Font("Arial", 20f, FontStyle.Bold), fntBrush, 40, 30);
        }
        //end of gui for home
        //gui for room page 
        else if (page == 1)
        {
            string img1="";
            string img2="";
            if (room == 0)//egypt
            {
                 img1 = "Untitled.jpg";
                 img2 = "Untitled1.jpg";
            }
            if (room==1)//china
            {
                img1 = "china1.jpg";
                img2 = "china2.jpg";
            }
            if (room==2)//europe
            {
                img1 = "eu1.jpg";
                img2 = "eu2.jpg";
            }
            
                int cW = 400, cH = 350, gap = 30;
                int totalW = cW * 3 + gap * 2;
                int startX = (this.ClientSize.Width - totalW) / 2;
                int startY = (this.ClientSize.Height - cH) / 2;

                // Preview
                g.FillRectangle(cardBsh, startX, startY, cW, cH);
                g.FillRectangle(new SolidBrush(Color.FromArgb(60, 60, 100)), startX + 10, startY + 10, cW - 20, cH - 50);
                g.DrawImage(Image.FromFile(img1), startX + 10, startY + 10, cW - 20, cH - 50);
                g.DrawString("Preview", new Font("Arial", 13f, FontStyle.Bold), fntBrush, startX + 14, startY + cH - 34);

                //  SCAN
                int midX = startX + cW + gap;
                g.FillRectangle(cardBsh, midX, startY, cW, cH);
                g.DrawString("SCAN", new Font("Arial", 48f, FontStyle.Bold), fntBrush,
                    midX + (cW / 2) - 70, startY + (cH / 2) - 30);

                //  Suggested
                int rightX = startX + (cW + gap) * 2;
                g.FillRectangle(cardBsh, rightX, startY, cW, cH);
                g.FillRectangle(new SolidBrush(Color.FromArgb(60, 60, 100)), rightX + 10, startY + 10, cW - 20, cH - 50);
                g.DrawImage(Image.FromFile(img2), rightX + 10, startY + 10, cW - 20, cH - 50);
                g.DrawString("Suggested", new Font("Arial", 13f, FontStyle.Bold), fntBrush, rightX + 14, startY + cH - 34);

                g.DrawString("Hello, " + uname, new Font("Arial", 20f, FontStyle.Bold), fntBrush, 40, 30);
                if (upic != null) g.DrawImage(upic, 60, 90, 100, 100);
                else g.FillEllipse(new SolidBrush(Color.FromArgb(80, 80, 120)), 60, 90, 100, 100);
            
            
        }//end of gui for room
        else if (page == 2 || page == 3||page==4)//gui for vistor of rooms but not in said room
        {
            int cW = 900, cH = 500;
            int cX = (this.ClientSize.Width - cW) / 2;
            int cY = (this.ClientSize.Height - cH) / 2;
            if(page==2)//china
            {
                slideImages = new string[] { "china1.jpg", "china2.jpg", "china3.jpg", "china4.jpg", "china5.jpg" };
            }
            if (page == 3)//medival europe
            {
                slideImages = new string[] { "eu1.jpg", "eu2.jpg", "eu3.jpg", "eu4.jpg", "eu5.jpg" };
            }
            if(page == 4) //egypt if u are not in gyipt
            {
                slideImages = new string[] { "egy1.jpg", "egy2.jpg", "egy3.jpg", "egy4.jpg", "egy5.jpg" };
            }
            g.FillRectangle(cardBsh, cX, cY, cW, cH);
            g.DrawImage(Image.FromFile(slideImages[slideIndex]), cX + 10, cY + 10, cW - 20, cH - 50);
            g.DrawString("News", new Font("Arial", 13f, FontStyle.Bold), fntBrush, cX + 14, cY + cH - 34);

            g.DrawString("Hello, " + uname, new Font("Arial", 20f, FontStyle.Bold), fntBrush, 40, 30);
            if (upic != null) g.DrawImage(upic, 60, 90, 100, 100);
            else g.FillEllipse(new SolidBrush(Color.FromArgb(80, 80, 120)), 60, 90, 100, 100);
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
                g.DrawString("Make a CIRCLE to add to favorites!", new Font("Arial", 12f, FontStyle.Bold), new SolidBrush(Color.FromArgb(255, 192, 203)), textX, cardY + cardH - 60);
                g.DrawString("SwipeRight: Home  |  SwipeLeft: News", new Font("Arial", 11f, FontStyle.Italic), new SolidBrush(Color.Silver), textX, cardY + cardH - 34);
            }
        }
        // favorites page
        else if (page == 6)
        {
            // Title
            g.DrawString("My Favorites", new Font("Arial", 28f, FontStyle.Bold), fntBrush, 50, 30);

            // Check if user is logged in
            if (currentUser == null || currentUser.favorites == null || currentUser.favorites.Count == 0)
            {
                g.DrawString("No favorites added yet. Open an artifact and make a CIRCLE gesture to add it to favorites!", 
                    new Font("Arial", 16f), fntBrush, 50, 150);
            }
            else
            {
                // Display favorite artifacts
                int cW = 280, cH = 350, gap = 30;
                int startX = 50;
                int startY = 120;
                int itemsPerRow = (this.ClientSize.Width - 80) / (cW + gap);
                
                for (int i = 0; i < currentUser.favorites.Count; i++)
                {
                    int artifactId = currentUser.favorites[i];
                    ArtifactRecord artifact = GetArtifactById(artifactId);
                    
                    if (artifact != null)
                    {
                        int row = i / itemsPerRow;
                        int col = i % itemsPerRow;
                        int x = startX + col * (cW + gap);
                        int y = startY + row * (cH + gap);

                        // Draw card background
                        g.FillRectangle(cardBsh, x, y, cW, cH);

                        // Draw artifact image
                        g.FillRectangle(new SolidBrush(Color.FromArgb(60, 60, 100)), x + 10, y + 10, cW - 20, cH - 50);
                        string imagePath = ResolveArtifactAssetPath(artifact.objPath);
                        if (File.Exists(imagePath))
                        {
                            Image artifactImage = Image.FromFile(imagePath);
                            g.DrawImage(artifactImage, x + 10, y + 10, cW - 20, cH - 50);
                            artifactImage.Dispose();
                        }

                        // Draw artifact name
                        g.DrawString(artifact.name, new Font("Arial", 11f, FontStyle.Bold), fntBrush,
                                     x + 14, y + cH - 34);
                    }
                }
            }

            // User info
            if (upic != null)
                g.DrawImage(upic, 60, 90, 100, 100);
            else
                g.FillEllipse(new SolidBrush(Color.FromArgb(80, 80, 120)), 60, 90, 100, 100);
            g.DrawString("Hello, " + uname, new Font("Arial", 20f, FontStyle.Bold), fntBrush, 40, 30);

            // Navigation hints
            g.DrawString("SwipeRight: Home  |  SwipeLeft: News", new Font("Arial", 11f, FontStyle.Italic), new SolidBrush(Color.Silver), 50, this.ClientSize.Height - 34);
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
