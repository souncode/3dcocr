using S7.Net;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using DirectShowLib;
using System.Threading;
using System.IO.Ports;
using System.IO;
using static System.Net.Mime.MediaTypeNames;
using System.Drawing.Drawing2D;
using Tesseract;
using System.Net;
namespace HPMML
{
    public partial class Dashboard : Form
    {

        #region Struct
        struct Video_Device
        {

            public string Device_Name;
            public int Device_ID;
            public Guid Identifier;


            public Video_Device(int ID, string Name, Guid Identity = new Guid())
            {
                Device_ID = ID;
                Device_Name = Name;
                Identifier = Identity;
            }

            public override string ToString()
            {
                return String.Format("[{0}] {1}: {2}", Device_ID, Device_Name, Identifier);
            }
        }
        #endregion

        #region Variables
        private Image<Bgr, byte> originalImage;
        private Image<Bgr, byte> denoisedImage;
        SerialPort serialPort = new SerialPort();
        const string plcIP = "192.168.1.1";
        const string arduinoPort = "COM3";
        string ocrChar;
        bool isAlarm;
        bool isPass;
        Bitmap croppedImage;
        Bitmap tempPb;
        float failVl;
        float passVl;
        int passCount = 0;
        int failCount = 0;
        int charCount;
        private Plc plc;
        int charLimit = 5;
        bool isLock;
        Bitmap currentFrame;
        Bitmap TempFrame;
        private VideoCapture _capture;
        Rectangle selectionRect = new Rectangle();
        System.Drawing.Point startPoint;
        Video_Device[] WebCams;
        public Image<Bgr, byte> ImgInput;
        Image<Bgr, byte> ImgOutput;
        public Dashboard()
        {
            InitializeComponent();
            SetRoundForm();
        }
        #endregion

        #region Function
        private void SetRoundForm()
        {
            GraphicsPath formShape = new GraphicsPath();
            Rectangle rect = new Rectangle(0, 0, this.Width, this.Height);
            int radius = 20;
            int diameter = radius * 2;
            formShape.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90); // Top left corner
            formShape.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90); // Top right corner
            formShape.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90); // Bottom right corner
            formShape.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90); // Bottom left corner
            formShape.CloseFigure();
            this.Region = new Region(formShape);
        }
        void getDevice()
        {
            DsDevice[] _SystemCamereas = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            WebCams = new Video_Device[_SystemCamereas.Length];
            for (int i = 0; i < _SystemCamereas.Length; i++)
            {
                WebCams[i] = new Video_Device(i, _SystemCamereas[i].Name, _SystemCamereas[i].ClassID);
                cbDevices.Items.Add(WebCams[i].ToString());
            }
            if (cbDevices.Items.Count > 0)
            {
                cbDevices.SelectedIndex = 0;
            }
        }
        void disableBtn(bool en)
        {
            //   btnProcess.Enabled = !en;
            btnTrigger.Enabled = !en;
            btnStart.Enabled = !en;
            btnHome.Enabled = !en;

        }
        void connectPLC()
        {
            plc = new Plc(CpuType.S71500, plcIP, 0, 0);
            plc.Open();
            string plcIp = plc.IP;
            cbPlc.Text = plcIp;
            if (plc.IsConnected)
            {
                MessageBox.Show("PLC Connected");
            }
            else
            {
                MessageBox.Show("Fail to connect to PLC");
            }
        }
        private void Dashboard_Load(object sender, EventArgs e)
        {
            getDevice();
            disableBtn(true);
        }
        private void ProcessFrame(object sender, EventArgs e)
        {
            Mat frame = new Mat();
            _capture.Retrieve(frame);
            Image<Rgba, byte> image = frame.ToImage<Rgba, byte>().Flip(Emgu.CV.CvEnum.FlipType.None);
            currentFrame = image.ToBitmap();
        }
        void connectCamera()
        {
            _capture = new VideoCapture(cbDevices.SelectedIndex);
            _capture.SetCaptureProperty(CapProp.FrameWidth, 2560);
            _capture.SetCaptureProperty(CapProp.FrameHeight, 1440);
            _capture.SetCaptureProperty(CapProp.Fps, 100);
            _capture.ImageGrabbed += ProcessFrame;
            _capture.Start();
            if (_capture.IsOpened)
            {
                MessageBox.Show("Camera Connected");
                btnTrigger.Enabled = true;
            }
            else
            {
                MessageBox.Show("Fail to connect to Camera");
            }
        }
        private void saveImage(bool isPass)
        {

            if (isPass)
            {
                try
                {
                    string currentTime = DateTime.Now.ToString("ddMMyy_hhmmss");
                    pbRaw.Image.Save(@"D:\DataFinal\Pass\" + currentTime + "_Raw" + ".jpg");
                    Console.WriteLine("Saved pass image");
                }
                catch (Exception)
                {
                    MessageBox.Show("Fail to save");
                }
            }
            else
            {
                try
                {
                    string currentTime = DateTime.Now.ToString("ddMMyy_hhmmss");
                    pbRaw.Image.Save(@"D:\DataFinal\Fail\" + currentTime + "_Raw" + ".jpg");
                    Console.WriteLine("Saved fail image");
                }
                catch (Exception)
                {
                    MessageBox.Show("Fail to save");
                }
            }
        }
        private bool getResult(int charCount, int charLimit)
        {
            if (charCount > charLimit)
            {
                isPass = true;
                passCount++;
                lbPass.Text = passCount.ToString();
                pbResult.Image = Properties.Resources.pass;
                lbTotal.Text = (passCount + failCount).ToString();
                return true;


            }
            else
            {
                isPass = false;
                failCount++;
                lbFail.Text = failCount.ToString();
                pbResult.Image = Properties.Resources.fail;
                lbTotal.Text = (passCount + failCount).ToString();
                return false;

            }

        }
        private string OCR(Bitmap b)
        {
            string res = "";
            using (var engine = new TesseractEngine(@"D:\Workspace\Winform\3DC\3DCORC\Vision_Solution\HPMML\OCRTrained", "eng", EngineMode.Default))
            {
                using (var page = engine.Process(b, PageSegMode.AutoOnly))
                    res = page.GetText();
            }
            return res;
        }
        private void ContrastBrightnessAdjust()
        {

            double currentConstrast = (float)trbConstrast.Value / 100;
            Bitmap sourceBitmap = new Bitmap(tempPb, pbRaw.Width, pbRaw.Height);
            try
            {

                ImgInput = sourceBitmap.ToImage<Bgr, byte>();
                ImgOutput = ImgInput.Mul((currentConstrast)) + trbBrightness.Value;

                pbRaw.Image = ImgOutput.AsBitmap();

            }

            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }

            Console.WriteLine("Cons :" + currentConstrast.ToString() + "------" + trbBrightness.Value.ToString());
        }
        #endregion

        private void btnExit_Click(object sender, EventArgs e)
        {
            this.Close();

        }
        private void sendToPlc(bool pass,string finish, string result)
        {
            if (plc.IsConnected)
            {
                if (pass)
                {
                    plc.Write(finish, false);
                    Thread.Sleep(10);
                    plc.Write(finish, true);
                    Thread.Sleep(10);
                    plc.Write(result, false);
                }
                else
                {
                    plc.Write(finish, false);
                    Thread.Sleep(10);
                    plc.Write(finish, true);
                    Thread.Sleep(10);
                    plc.Write(result, true);
                }
            }
        }
        private bool readFromPlc(string address)
        {
                object M01 = plc.Read(address);
                bool M01c = Convert.ToBoolean(M01);
                Console.WriteLine(M01c);
                if (M01c)
                {
                    return true;
                }
                else
                {
                    return false;
                } 
        }
        private void btnHome_Click(object sender, EventArgs e)
        {
           
        }

        private void btnTrigger_Click(object sender, EventArgs e)
        {
            try
            {
                if (currentFrame != null)
                {
                    pbRaw.Image = currentFrame;
                    plc.Write("M20.2", true);
                    Thread.Sleep(50);
                    plc.Write("M20.2", false);
                    btnProcess.PerformClick();
                }
                else
                {
                    MessageBox.Show("Null");
                }
            }
            catch (Exception)
            {
                MessageBox.Show(e.ToString());
            }
        }
    

        private void btnConnect_Click(object sender, EventArgs e)
        {
            connectPLC();
            connectCamera();
            disableBtn(false);
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            timerGetTrigger.Start();
            pbStt.Image = Properties.Resources.run;
        }

        private void btnProcess_Click(object sender, EventArgs e)
        {
            croppedImage = new Bitmap(selectionRect.Width, selectionRect.Height);
            Bitmap sourceBitmap = new Bitmap(pbRaw.Image, pbRaw.Width, pbRaw.Height);
            using (Graphics g = Graphics.FromImage(croppedImage))
            {
                g.DrawImage(sourceBitmap, new Rectangle(0, 0, croppedImage.Width, croppedImage.Height),
                            selectionRect, GraphicsUnit.Pixel);
            }
            ImgInput = croppedImage.ToImage<Bgr, byte>();
            ImgOutput = ImgInput.Mul(1.5).SmoothGaussian(3);
            Mat grayImage = new Mat();
            CvInvoke.CvtColor(ImgOutput, grayImage, ColorConversion.Bgr2Gray);
            Mat openedImage = new Mat();
            Mat kernel = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new Size(5, 5), new Point(-1, -1));
            CvInvoke.MorphologyEx(grayImage, openedImage, MorphOp.Erode, kernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());
            denoisedImage = openedImage.ToImage<Bgr, byte>();
            test.Image = denoisedImage.ToBitmap();
            ocrChar = OCR(denoisedImage.AsBitmap());
            test.Image=ImgOutput.AsBitmap();
            rtbResult.Text = ocrChar;
            getResult(ocrChar.Length, charLimit);

            sendToPlc(isPass,"Q0.6","Q0.7");
            // saveImage(isPass);
        }

        private void timerGetTrigger_Tick(object sender, EventArgs e)
        {
            if (plc.IsConnected)
            {
                if (readFromPlc("M20.1"))
                {
                    //600 O
                    Thread.Sleep(100);
                    btnTrigger.PerformClick();
                }
            }
            else
            {
                Console.WriteLine("PLC not connect");
            }
        }

        private void pbRaw_MouseDown(object sender, MouseEventArgs e)
        {
            if (!isLock)
            {
                if (e.Button == MouseButtons.Left)
                {
                    startPoint = e.Location;
                }
            }
            else { MessageBox.Show("Locked"); }
           
         
        }

        private void pbRaw_Paint(object sender, PaintEventArgs e)
        {
            if (pbRaw.Image != null)
            {
                using (Pen pen = new Pen(Color.Red, 2))
                {
                    e.Graphics.DrawRectangle(pen, selectionRect);
                }
            }
        }

        private void pbRaw_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isLock)
            {
                if (e.Button == MouseButtons.Left)
                {
                    int x = Math.Min(startPoint.X, e.X);
                    int y = Math.Min(startPoint.Y, e.Y);
                    int width = Math.Abs(startPoint.X - e.X);
                    int height = Math.Abs(startPoint.Y - e.Y);
                    selectionRect = new Rectangle(x, y, width, height);
                    pbRaw.Refresh();
                }
            }
        }

        private void btnMonitor_Click(object sender, EventArgs e)
        {

            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Filter = "Image files (*.jpg, *.jpeg, *.png, *.gif, *.bmp)|*.jpg;*.jpeg;*.png;*.gif;*.bmp|All files (*.*)|*.*";
                openFileDialog.FilterIndex = 1;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string filePath = openFileDialog.FileName;
                        System.Drawing.Image image = System.Drawing.Image.FromFile(filePath);
                        tempPb = (Bitmap)image;
                        pbRaw.Image = image;
                        var imageBytes = File.ReadAllBytes(filePath);
                      
                        
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error: Could not open image file. Original error: " + ex.Message);
                    }
                }
            }
        }

        private void trbConstrast_Scroll(object sender, EventArgs e)
        {
            try
            {
                ContrastBrightnessAdjust();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            lbConstrast.Text = trbConstrast.Value.ToString();
        }

        private void trbBrightness_Scroll(object sender, EventArgs e)
        {
            lbBrightness.Text = trbBrightness.Value.ToString();
            try
            {
                ContrastBrightnessAdjust();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
    }
}
