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

	public class TuioDemo : Form , TuioListener
	{
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

		// Face detection status
		private bool faceDetected = false;
		private string lastMessage = "";
		private DateTime lastFaceDetectionTime = DateTime.Now;
		
		// UI Components
		private Label statusLabel;
		private Label markerInfoLabel;
		private Label faceStatusLabel;

		Font font = new Font("Arial", 10.0f);
		Font titleFont = new Font("Arial", 16.0f, FontStyle.Bold);
		Font statusFont = new Font("Arial", 12.0f);
		SolidBrush fntBrush = new SolidBrush(Color.White);
		SolidBrush bgrBrush = new SolidBrush(Color.FromArgb(0,0,64));
		SolidBrush curBrush = new SolidBrush(Color.FromArgb(192, 0, 192));
		SolidBrush objBrush = new SolidBrush(Color.FromArgb(64, 0, 0));
		SolidBrush blbBrush = new SolidBrush(Color.FromArgb(64, 64, 64));
		SolidBrush faceDetectedBrush = new SolidBrush(Color.FromArgb(0, 255, 0));
		SolidBrush faceNotDetectedBrush = new SolidBrush(Color.FromArgb(255, 100, 100));
		Pen curPen = new Pen(new SolidBrush(Color.Blue), 1);

		public TuioDemo(int port) {
		
			verbose = false;
			fullscreen = false;
			width = window_width;
			height = window_height;

			this.ClientSize = new System.Drawing.Size(width, height);
			this.Name = "TuioDemo";
			this.Text = "MuseSense - Interactive Museum Guide";
			
			this.Closing+=new CancelEventHandler(Form_Closing);
			this.KeyDown+=new KeyEventHandler(Form_KeyDown);

			this.SetStyle( ControlStyles.AllPaintingInWmPaint |
							ControlStyles.UserPaint |
							ControlStyles.DoubleBuffer, true);

			// Initialize UI labels
			InitializeLabels();

			objectList = new Dictionary<long,TuioObject>(128);
			cursorList = new Dictionary<long,TuioCursor>(128);
			blobList   = new Dictionary<long,TuioBlob>(128);
			
			client = new TuioClient(port);
			client.addTuioListener(this);

			client.connect();
			
			// Start socket thread for Python communication (face detection, gestures)
			Thread socketThread = new Thread(stream);
			socketThread.IsBackground = true;
			socketThread.Start();
    }
    
    private void InitializeLabels()
    {
        // Face status label (top-right corner)
        faceStatusLabel = new Label();
        faceStatusLabel.AutoSize = false;
        faceStatusLabel.Size = new Size(200, 30);
        faceStatusLabel.Location = new Point(width - 210, 10);
        faceStatusLabel.BackColor = Color.Transparent;
        faceStatusLabel.ForeColor = Color.White;
        faceStatusLabel.Font = statusFont;
        faceStatusLabel.Text = "Face: Not Detected";
        this.Controls.Add(faceStatusLabel);
        
        // Marker info label (top-left corner)
        markerInfoLabel = new Label();
        markerInfoLabel.AutoSize = false;
        markerInfoLabel.Size = new Size(300, 60);
        markerInfoLabel.Location = new Point(10, 10);
        markerInfoLabel.BackColor = Color.Transparent;
        markerInfoLabel.ForeColor = Color.White;
        markerInfoLabel.Font = statusFont;
        markerInfoLabel.Text = "Place a marker to begin";
        this.Controls.Add(markerInfoLabel);
        
        // Status label (bottom center)
        statusLabel = new Label();
        statusLabel.AutoSize = false;
        statusLabel.Size = new Size(400, 25);
        statusLabel.Location = new Point((width - 400) / 2, height - 35);
        statusLabel.BackColor = Color.Transparent;
        statusLabel.ForeColor = Color.LightGray;
        statusLabel.Font = font;
        statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        statusLabel.Text = "Press F1 for fullscreen | ESC to exit | V for verbose";
        this.Controls.Add(statusLabel);
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
			} 
			if (verbose) Console.WriteLine("add obj "+o.SymbolID+" ("+o.SessionID+") "+o.X+" "+o.Y+" "+o.Angle);
			
			// Update marker info on UI thread
			UpdateMarkerInfo(o.SymbolID, true);
		}

		public void updateTuioObject(TuioObject o) {

			if (verbose) Console.WriteLine("set obj "+o.SymbolID+" "+o.SessionID+" "+o.X+" "+o.Y+" "+o.Angle+" "+o.MotionSpeed+" "+o.RotationSpeed+" "+o.MotionAccel+" "+o.RotationAccel);
		}

		public void removeTuioObject(TuioObject o) {
			lock(objectList) {
				objectList.Remove(o.SessionID);
			}
			if (verbose) Console.WriteLine("del obj "+o.SymbolID+" ("+o.SessionID+")");
			
			// Update marker info on UI thread
			if (objectList.Count == 0) {
				UpdateMarkerInfo(-1, false);
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
        int byteCT;
        public NetworkStream stream;
        byte[] sendData;
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
            catch (System.Exception e)
            {

            }

            return null;
        }

    }
    public void stream()
    {
        Console.WriteLine("Starting socket connection for Python communication...");
        Client c = new Client();
        
        if (!c.connectToSocket("localhost", 5000))
        {
            Console.WriteLine("Failed to connect to Python server. Make sure Python script is running.");
            UpdateStatus("Python connection failed - Face detection unavailable");
            return;
        }
        
        UpdateStatus("Connected to Python server");
        string msg = "";
        
        while (true)
        {
            msg = c.recieveMessage();
            
            if (msg == null || msg == "q")
            {
                c.stream.Close();
                c.client.Close();
                Console.WriteLine("Connection Terminated !");
                UpdateStatus("Python connection closed");
                break;
            }
            
            // Process the message from Python
            Console.WriteLine("Received from Python: " + msg);
            
            if (msg.Contains("face:detected") || msg.Contains("face detected"))
            {
                faceDetected = true;
                lastFaceDetectionTime = DateTime.Now;
                UpdateFaceStatus(true);
            }
            else if (msg.Contains("face:lost") || msg.Contains("face lost"))
            {
                faceDetected = false;
                UpdateFaceStatus(false);
            }
            else if (msg.Contains("gesture"))
            {
                UpdateStatus("Gesture detected: " + msg);
            }
            
            lastMessage = msg;
            
            // Request UI refresh
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new MethodInvoker(delegate { this.Invalidate(); }));
            }
            else
            {
                this.Invalidate();
            }
            
            Thread.Sleep(50); // Small delay to prevent CPU overload
        }
    }
    
    private void UpdateFaceStatus(bool detected)
    {
        if (faceStatusLabel.InvokeRequired)
        {
            faceStatusLabel.BeginInvoke(new MethodInvoker(delegate {
                faceStatusLabel.Text = detected ? "✓ Face Detected" : "Face: Not Detected";
                faceStatusLabel.ForeColor = detected ? Color.LightGreen : Color.LightCoral;
            }));
        }
        else
        {
            faceStatusLabel.Text = detected ? "✓ Face Detected" : "Face: Not Detected";
            faceStatusLabel.ForeColor = detected ? Color.LightGreen : Color.LightCoral;
        }
    }
    
    private void UpdateStatus(string message)
    {
        if (statusLabel.InvokeRequired)
        {
            statusLabel.BeginInvoke(new MethodInvoker(delegate {
                statusLabel.Text = message;
            }));
        }
        else
        {
            statusLabel.Text = message;
        }
    }
    
    private void UpdateMarkerInfo(int symbolID, bool added)
    {
        if (markerInfoLabel.InvokeRequired)
        {
            markerInfoLabel.BeginInvoke(new MethodInvoker(delegate {
                UpdateMarkerInfoInternal(symbolID, added);
            }));
        }
        else
        {
            UpdateMarkerInfoInternal(symbolID, added);
        }
    }
    
    private void UpdateMarkerInfoInternal(int symbolID, bool added)
    {
        if (!added)
        {
            markerInfoLabel.Text = "Place a marker to begin";
            markerInfoLabel.ForeColor = Color.White;
            return;
        }
        
        string artifactName = "";
        string artifactInfo = "";
        
        switch (symbolID)
        {
            case 0:
                artifactName = "Artifact #0";
                artifactInfo = "Ancient Pottery Collection";
                break;
            case 1:
                artifactName = "Artifact #1";
                artifactInfo = "Medieval Manuscript";
                break;
            case 2:
                artifactName = "Artifact #2";
                artifactInfo = "Renaissance Painting";
                break;
            default:
                artifactName = $"Marker #{symbolID}";
                artifactInfo = "Unknown Artifact";
                break;
        }
        
        markerInfoLabel.Text = $"📍 {artifactName}\n{artifactInfo}";
        markerInfoLabel.ForeColor = Color.Yellow;
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
		{
			// Getting the graphics object
			Graphics g = pevent.Graphics;
			g.FillRectangle(bgrBrush, new Rectangle(0,0,width,height));

			// Draw face detection indicator in top-right corner
			int indicatorSize = 15;
			int indicatorX = width - 220;
			int indicatorY = 45;
			if (faceDetected && (DateTime.Now - lastFaceDetectionTime).TotalSeconds < 2)
			{
				g.FillEllipse(faceDetectedBrush, indicatorX, indicatorY, indicatorSize, indicatorSize);
			}
			else
			{
				g.FillEllipse(faceNotDetectedBrush, indicatorX, indicatorY, indicatorSize, indicatorSize);
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
			if (objectList.Count > 0) {
 				lock(objectList) {
					foreach (TuioObject tobj in objectList.Values) {
                    int size = height / 10;
					int ox=0;
					int oy=0;
                    
                    // Draw glow effect around marker to show it's detected
                    ox = tobj.getScreenX(width);
                    oy = tobj.getScreenY(height);
                    
                    // Draw outer glow
                    using (Pen glowPen = new Pen(Color.FromArgb(100, 255, 255, 0), 3))
                    {
                        g.DrawEllipse(glowPen, ox - size/2 - 10, oy - size/2 - 10, size + 20, size + 20);
                    }
                    
                    if (tobj.SymbolID == 0)
                    {
                        try {
                            Image bgimg = Image.FromFile("background1.png");
                            Image objimg = Image.FromFile("obj1.png");
                            g.DrawImage(bgimg, 0, 0, width, height);
                            g.TranslateTransform(ox, oy);
                            g.RotateTransform((float)(tobj.Angle / Math.PI * 180.0f));
                            g.TranslateTransform(-ox, -oy);
                       

                            g.DrawImage(objimg, ox - size / 2, oy - size / 2, size, size);
                        } catch (Exception ex) {
                            // If images not found, draw colored rectangle
                            g.FillRectangle(new SolidBrush(Color.FromArgb(180, 100, 50)), new Rectangle(ox - size / 2, oy - size / 2, size, size));
                        }
                    }
                    else if (tobj.SymbolID == 1)
                    {
                        try {
                            Image bgimg = Image.FromFile("background2.png");
                            Image objimg = Image.FromFile("obj2.png");
                            g.DrawImage(bgimg, 0, 0, width, height);
                            g.TranslateTransform(ox, oy);
                            g.RotateTransform((float)(tobj.Angle / Math.PI * 180.0f));
                            g.TranslateTransform(-ox, -oy);

                            g.DrawImage(objimg, ox - size / 2, oy - size / 2, size, size);
                        } catch (Exception ex) {
                            g.FillRectangle(new SolidBrush(Color.FromArgb(50, 100, 180)), new Rectangle(ox - size / 2, oy - size / 2, size, size));
                        }
                    }
                    else if (tobj.SymbolID == 2)
                    {
                        try {
                            Image bgimg = Image.FromFile("background3.png");
                            Image objimg = Image.FromFile("obj3.png");
                            g.DrawImage(bgimg, 0, 0, width, height);
                            g.TranslateTransform(ox, oy);
                            g.RotateTransform((float)(tobj.Angle / Math.PI * 180.0f));
                            g.TranslateTransform(-ox, -oy);

                            g.DrawImage(objimg, ox - size / 2, oy - size / 2, size, size);
                        } catch (Exception ex) {
                            g.FillRectangle(new SolidBrush(Color.FromArgb(180, 50, 180)), new Rectangle(ox - size / 2, oy - size / 2, size, size));
                        }
                    }
					else //default marker
					{
                        size = height / 10;

                        g.TranslateTransform(ox, oy);
                        g.RotateTransform((float)(tobj.Angle / Math.PI * 180.0f));
                        g.TranslateTransform(-ox, -oy);

                        // Draw colored rectangle based on symbol ID
                        Color markerColor = Color.FromArgb(
                            (tobj.SymbolID * 40) % 255, 
                            (tobj.SymbolID * 80) % 255, 
                            (tobj.SymbolID * 120) % 255
                        );
                        g.FillRectangle(new SolidBrush(markerColor), new Rectangle(ox - size / 2, oy - size / 2, size, size));
                    }

                    // Draw marker ID text
                    g.TranslateTransform(ox, oy);
					g.RotateTransform(-1 * (float)(tobj.Angle / Math.PI * 180.0f));
					g.TranslateTransform(-ox, -oy);

					// Draw ID with background for better visibility
					string idText = "ID:" + tobj.SymbolID;
					SizeF textSize = g.MeasureString(idText, font);
					g.FillRectangle(new SolidBrush(Color.FromArgb(150, 0, 0, 0)), 
					    new RectangleF(ox - textSize.Width/2 - 2, oy - textSize.Height/2 - 2, 
					    textSize.Width + 4, textSize.Height + 4));
					g.DrawString(idText, font, fntBrush, new PointF(ox - textSize.Width/2, oy - textSize.Height/2));
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
            this.SuspendLayout();
            // 
            // TuioDemo
            // 
            this.ClientSize = new System.Drawing.Size(282, 253);
            this.Name = "TuioDemo";
            this.Load += new System.EventHandler(this.TuioDemo_Load);
            this.ResumeLayout(false);

    }
	 
    private void TuioDemo_Load(object sender, EventArgs e)
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
