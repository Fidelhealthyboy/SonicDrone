﻿using DroneMonitor.Infrastructure.Base;
using DroneMonitor.Visualization.Markers;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;
using Prism.Commands;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DroneMonitor.Visualization.ViewModels {
    public class VisualizationViewModel : ViewModelBase {
        public VisualizationViewModel() {
            Ports = SerialPort.GetPortNames();
            _serialPort = new SerialPort();
            _serialPort.PortName = "default";
            Maps = GMapProviders.List;
            SelectedMap = GMapProviders.GoogleMap;
            OpenCloseCommand = new DelegateCommand(OpenCloseAction);
            ResetCommand = new DelegateCommand(ResetAction);
            FlyListCommand = new DelegateCommand(FlyListAction);
            CreateListCommand = new DelegateCommand(CreateListAction, () => {
                return start == 2 && flight_mode == 3 && fly_waypoint_list.Enabled == false;
            });
            _alwaysRunningTimer = new Timer(2000);
            _alwaysRunningTimer.Elapsed += AlwaysRunning;
            fly_waypoint_list = new Timer(2000);
            fly_waypoint_list.Elapsed += Fly_waypoint_list_Tick;
            Send_telemetry_data = new Timer(200);
            Send_telemetry_data.Elapsed += Send_telemetry_data_Tick;
            _flight_timer = new Timer(200);
            _flight_timer.Elapsed += FlightTimerTick;
            _alwaysRunningTimer.Start();
            fly_waypoint_list.Start();
            Send_telemetry_data.Start();
        }

        public void MapClicked(GMapControl sender, MouseButtonEventArgs e) {
            Map = sender;
            var point = e.GetPosition(sender);
            if (_serialPort.IsOpen && first_receive == 1 && start == 2 && flight_mode == 3 && fly_waypoint_list.Enabled == false) {
                var latLong = sender.FromLocalToLatLng((int)point.X, (int)point.Y);

                if (create_waypoint_list && waypoint_list_counter < 8) {
                    var marker = new GMapMarker(latLong) {
                        Offset = new Point(-15, -15),
                        ZIndex = int.MaxValue,
                    };
                    marker.Shape = new GreenMarker(marker, (waypoint_list_counter + 1).ToString());
                    sender.Markers.Add(marker);

                }
                else {

                    if (_marker != null)
                        sender.Markers.Remove(_marker);

                    _marker = new GMapMarker(latLong) {
                        Offset = new Point(-15, -15),
                        ZIndex = int.MaxValue,
                    };
                    _marker.Shape = new GreenMarker(_marker, "w");
                    sender.Markers.Add(_marker);
                }

                click_lat = (int)(home_lat_gps + latLong.Lat);
                click_lon = (int)(home_lon_gps + latLong.Lng);

                send_buffer[0] = (byte)'W';
                send_buffer[1] = (byte)'P';

                send_buffer[5] = (byte)(click_lat >> 24);
                send_buffer[4] = (byte)(click_lat >> 16);
                send_buffer[3] = (byte)(click_lat >> 8);
                send_buffer[2] = (byte)(click_lat);

                send_buffer[9] = (byte)(click_lon >> 24);
                send_buffer[8] = (byte)(click_lon >> 16);
                send_buffer[7] = (byte)(click_lon >> 8);
                send_buffer[6] = (byte)(click_lon);

                send_buffer[10] = (byte)'-';
                check_byte = 0;
                for (temp_byte = 0; temp_byte <= 10; temp_byte++) {
                    check_byte ^= send_buffer[temp_byte];
                }
                send_buffer[11] = check_byte;

                if (create_waypoint_list) {
                    if (waypoint_list_counter < 8) waypoint_list_counter++;
                    waypoint_click_lat[waypoint_list_counter] = click_lat;
                    waypoint_click_lon[waypoint_list_counter] = click_lon;
                }
                else {
                    if (_serialPort.IsOpen) {
                        _serialPort.Write(send_buffer, 0, 13);
                    }
                    SendStatus = "Try 1";
                    new_telemetry_data_to_send = 1;
                    Send_telemetry_data.Enabled = true;
                }
            }
        }

        private void FlyListAction() {
            send_telemetry_data_counter = 1;
            waypoint_send_step = 1;
            fly_waypoint_list.Enabled = true;
        }

        private void ResetAction() {
            create_waypoint_list = false;
            fly_waypoint_list.Enabled = false;
            Send_telemetry_data.Enabled = false;
            SendStatus = "-";
            ClearWayPointMarkers();
        }

        private void CreateListAction() {
            ClearWayPointMarkers();
            create_waypoint_list = true;
            waypoint_list_counter = 0;
        }

        private void OpenCloseAction() {
            if (!IsOpen) {
                _dataProgress = new Progress<bool>(val =>
                {
                    if (val)
                        AlwaysRunning();
                });

                if (!string.IsNullOrWhiteSpace(SelectedPort)) {
                    _serialPort = new SerialPort(SelectedPort, 9600, Parity.None, 8, StopBits.One);
                    _serialPort.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);
                    _serialPort.Open();
                    IsOpen = true;
                    StartStop = "Stop";
                }
                else {
                    WindowService.DisplayAlert("Error", "No port selected");
                    return;
                }
            }
            else {
                _serialPort.Close();
                CanShowHomeMarker = false;
                first_receive = 0;
                CreateListCommand.RaiseCanExecuteChanged();
                IsOpen = false;
                StartStop = "Start";
                _flight_timer.Enabled = false;
            }
        }

        private void ClearWayPointMarkers() {
            if (Map != null) {
                var wayPointMarkers = Map.Markers.Where(x => x.Shape is GreenMarker);
                foreach (var marker in wayPointMarkers) {
                    Map.Markers.Remove(marker);
                }
            }
        }

        private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e) {
            if (received_data == 0)
                received_data = 1;
            int nextByte = _serialPort.ReadByte();
            if (nextByte >= 0)
                receive_buffer[receive_buffer_counter] = (byte)nextByte;

            if (receive_byte_previous == 'J' && receive_buffer[receive_buffer_counter] == 'B') {
                receive_buffer_counter = 0;
                receive_start_detect++;
                if (receive_start_detect >= 2)
                    get_data();
            }
            else {
                receive_byte_previous = receive_buffer[receive_buffer_counter];
                receive_buffer_counter++;
                if (receive_buffer_counter > 48)
                    receive_buffer_counter = 0;
            }
        }

        private void get_data() {
            check_byte = 0;
            for (temp_byte = 0; temp_byte <= 30; temp_byte++)
                check_byte ^= receive_buffer[temp_byte];

            if (check_byte == receive_buffer[31]) {
                first_receive = 1;
                last_receive = milliseconds;
                receive_start_detect = 1;

                error = receive_buffer[0];
                flight_mode = receive_buffer[1];
                battery_voltage = (float)receive_buffer[2] / 10.0f;
                battery_bar_level = receive_buffer[2];

                temperature = (short)(receive_buffer[3] | receive_buffer[4] << 8);
                roll_angle = receive_buffer[5] - 100;
                pitch_angle = receive_buffer[6] - 100;
                start = receive_buffer[7];
                altitude_meters = (receive_buffer[8] | receive_buffer[9] << 8) - 1000;
                if (altitude_meters > max_altitude_meters) max_altitude_meters = altitude_meters;
                takeoff_throttle = receive_buffer[10] | receive_buffer[11] << 8;
                actual_compass_heading = receive_buffer[12] | receive_buffer[13] << 8;
                heading_lock = receive_buffer[14];
                number_used_sats = receive_buffer[15];
                fix_type = receive_buffer[16];
                l_lat_gps = receive_buffer[17] | receive_buffer[18] << 8 | receive_buffer[19] << 16 | receive_buffer[20] << 24;
                l_lon_gps = receive_buffer[21] | receive_buffer[22] << 8 | receive_buffer[23] << 16 | receive_buffer[24] << 24;

                adjustable_setting_1 = (receive_buffer[25] | receive_buffer[26] << 8) / 100.0f;
                adjustable_setting_2 = (receive_buffer[27] | receive_buffer[28] << 8) / 100.0f;
                adjustable_setting_3 = (receive_buffer[29] | receive_buffer[30] << 8) / 100.0f;
                ground_distance = Math.Pow((float)((l_lat_gps - home_lat_gps) ^ 2) * 0.111, 2);
                ground_distance += Math.Pow((float)(l_lon_gps - home_lon_gps) * (Math.Cos((l_lat_gps / 1000000) * 0.017453) * 0.111), 2);
                ground_distance = Math.Sqrt(ground_distance);
                los_distance = Math.Sqrt(Math.Pow(ground_distance, 2) + Math.Pow(altitude_meters, 2));
                _dataProgress.Report(true);
            }
        }

        private void AlwaysRunning()
        {
            CreateListCommand.RaiseCanExecuteChanged();

            if (start != 2)
                WayPointVisibility = false;

            if (IsOpen && first_receive == 0)
            {
                SignalMessage = "Waiting for signal";
                SignalMessageVisibility = true;
            }
            else
            {
                SignalMessage = "";
                SignalMessageVisibility = false;
            }

            milliseconds += 100;
            if (first_receive == 1)
            {
                if (milliseconds - last_receive > 2000)
                {
                    SignalMessage = "Connection Lost";
                    SignalMessageVisibility = true;
                }
                if (milliseconds - last_receive < 1000)
                {
                    SignalMessage = "";
                    SignalMessageVisibility = false;
                }

                if (flight_mode == 1) FlightMode = "1-Auto level";
                if (flight_mode == 2) FlightMode = "2-Altutude hold";
                if (flight_mode == 3) FlightMode = "3-GPS hold";
                if (flight_mode == 4) FlightMode = "4-RTH active";
                if (flight_mode == 5) FlightMode = "5-RTH Increase altitude";
                if (flight_mode == 6) FlightMode = "6-RTH Returning to home position";
                if (flight_mode == 7) FlightMode = "7-RTH Landing";
                if (flight_mode == 8) FlightMode = "8-RTH finished";
                if (flight_mode == 9) FlightMode = "9-Fly to waypoint";

                if (start == 0)
                {
                    BladeColor = Brushes.Blue;
                }
                if (start == 1)
                {
                    BladeColor = Brushes.Yellow;
                }
                if (start == 2)
                {
                    BladeColor = Brushes.Red;
                }


                if (error == 0) Error = "No error";
                if (error == 1) Error = "Battery LOW";
                if (error == 2) Error = "Program loop time";
                if (error == 3) Error = "ACC cal error";
                if (error == 4) Error = "GPS watchdog time";
                if (error == 5) Error = "Manual take-off thr error";
                if (error == 6) Error = "No take-off detected";
                if (error == 7) Error = "Auto throttle error";

                SatUsed = number_used_sats.ToString();
                if (number_used_sats > 6)
                {
                    SatColor = Brushes.Green;

                }
                else if (number_used_sats > 3)
                {
                    SatColor = Brushes.Yellow;

                }
                else
                {
                    SatColor = Brushes.Red;

                }

                Latitude = ((float)l_lat_gps / 1000000.0).ToString(new CultureInfo("en-US"));
                Longitude = ((float)l_lon_gps / 1000000.0).ToString(new CultureInfo("en-US"));
                Heading = actual_compass_heading.ToString();
                Altitude = altitude_meters.ToString() + "m";
                MaxAltitude = max_altitude_meters.ToString() + "m";
                Battery = battery_voltage.ToString("00.0") + "V";
                PitchAngle = pitch_angle.ToString();
                RollAngle = roll_angle.ToString();
                Temperature = (temperature / 340.0 + 36.53).ToString("00.0") + "C";


                if (battery_voltage > 12) 
                    BatteryColor = Brushes.Lime;
                else if (battery_voltage > 11) 
                    BatteryColor = Brushes.Yellow;
                else 
                    BatteryColor = Brushes.Red;

                BatteryWidth =  (battery_voltage -10) * BorderWidth / (12.6 - 10);

                if (home_gps_set == 1)
                    LOSDistance = los_distance.ToString("0.") + "m";
                else
                    LOSDistance = "0m";

                if (home_gps_set == 0 && number_used_sats > 4 && start == 2)
                {
                    home_gps_set = 1;
                    home_lat_gps = l_lat_gps;
                    home_lon_gps = l_lon_gps;
                    CanShowHomeMarker = true;
                }

                if (home_gps_set == 1 && start == 0)
                {
                    home_gps_set = 0;
                    CanShowHomeMarker = false;
                }

                if (start == 2)
                {
                    if (_flight_timer.Enabled == false) _flight_timer.Enabled = true;
                }
                if (start == 0)
                {
                    if (_flight_timer.Enabled == true) _flight_timer.Enabled = false;
                }
            }

            _serialPort.Close();
            if (_serialPort.PortName != "default")
                _serialPort.Open();
        }

        private void AlwaysRunning(object sender, EventArgs e) {
            CreateListCommand.RaiseCanExecuteChanged();

            if (start != 2)
                WayPointVisibility = false;

            if (IsOpen && first_receive == 0) {
                SignalMessage = "Waiting for signal";
                SignalMessageVisibility = true;
            }
            else {
                SignalMessage = "";
                SignalMessageVisibility = false;
            }

            milliseconds += 100;
            if (first_receive == 1) {
                if (milliseconds - last_receive > 2000) {
                    SignalMessage = "Connection Lost";
                    SignalMessageVisibility = true;
                }
                if (milliseconds - last_receive < 1000) {
                    SignalMessage = "";
                    SignalMessageVisibility = false;
                } 
            }
        }

        private void FlightTimerTick(object sender, EventArgs e) {
            flight_timer_seconds++;
            FlightTimer = $"Flight Time: {"00:" + (flight_timer_seconds / 60).ToString("00.") + ":" + (flight_timer_seconds % 60).ToString("00.")}";
        }

        private void Fly_waypoint_list_Tick(object sender, EventArgs e) {// Continue running this once way point is clicked
            if (flight_mode != 3 && flight_mode != 9) {
                fly_waypoint_list.Enabled = false;
                SendStatus = "Aborted";
            }

            if (waypoint_send_step == 1) {
                click_lat = waypoint_click_lat[send_telemetry_data_counter];
                click_lon = waypoint_click_lon[send_telemetry_data_counter];

                new_telemetry_data_to_send = 1;

                send_buffer[0] = (byte)'W';
                send_buffer[1] = (byte)'P';

                send_buffer[5] = (byte)(click_lat >> 24);
                send_buffer[4] = (byte)(click_lat >> 16);
                send_buffer[3] = (byte)(click_lat >> 8);
                send_buffer[2] = (byte)(click_lat);

                send_buffer[9] = (byte)(click_lon >> 24);
                send_buffer[8] = (byte)(click_lon >> 16);
                send_buffer[7] = (byte)(click_lon >> 8);
                send_buffer[6] = (byte)(click_lon);

                send_buffer[10] = (byte)'-';
                check_byte = 0;
                for (temp_byte = 0; temp_byte <= 10; temp_byte++) {
                    check_byte ^= send_buffer[temp_byte];
                }
                send_buffer[11] = check_byte;

                Send_telemetry_data.Enabled = true;
                waypoint_send_step = 2;
            }
            if (waypoint_send_step == 3) {
                if (flight_mode == 3) waypoint_send_step = 4;
            }
            if (waypoint_send_step == 4) {
                if (waypoint_list_counter == send_telemetry_data_counter) {
                    SendStatus = "Waypoints ready";
                    fly_waypoint_list.Enabled = false;
                }
                else {
                    waypoint_send_step = 1;
                    send_telemetry_data_counter++;
                }
            }
        }

        private void Send_telemetry_data_Tick(object sender, EventArgs e) {

            if (flight_mode == 3 && new_telemetry_data_to_send > 0 && new_telemetry_data_to_send <= 10) {
                if (_serialPort.IsOpen) {
                    _serialPort.Write(send_buffer, 0, 13);
                    new_telemetry_data_to_send++;
                    SendStatus = "Try " + new_telemetry_data_to_send.ToString();

                }
            }
            else {
                new_telemetry_data_to_send = 0;
                Send_telemetry_data.Enabled = false;
                if (flight_mode == 3) SendStatus = "Fail";
                if (flight_mode == 9) {
                    SendStatus = "Received";
                    if (waypoint_send_step == 2) waypoint_send_step = 3;
                }
            }
        }

        private void ShowHomeMarker() {
            if (CanShowHomeMarker) {
                if (_homeMarker != null)
                    Map.Markers.Remove(_marker);

                _homeMarker = new GMapMarker(HomePosition) {
                    Offset = new Point(-15, -15),
                    ZIndex = int.MaxValue,
                };
                _homeMarker.Shape = new GreenMarker(_marker, "H");
                Map.Markers.Add(_homeMarker);
            }
            else {

                if (_homeMarker != null)
                    Map.Markers.Remove(_marker);
            }
        }

        #region Properties

        private double _borderWidth=100;
        public double BorderWidth
        {
            get => _borderWidth;
            set => SetProperty(ref _borderWidth, value);
        }

        private bool _isOpen = false;
        public bool IsOpen {
            get => _isOpen;
            set => SetProperty(ref _isOpen, value);
        }

        private string _startStop = "Start";
        public string StartStop {
            get => _startStop;
            set => SetProperty(ref _startStop, value);
        }

        private string _sendStatus;
        public string SendStatus {
            get => _sendStatus;
            set => SetProperty(ref _sendStatus, value);
        }

        private string _fligtMode;
        public string FlightMode {
            get => _fligtMode;
            set => SetProperty(ref _fligtMode, value);
        }

        private string _satUsed;
        public string SatUsed {
            get => _satUsed;
            set => SetProperty(ref _satUsed, value);
        }

        private Brush _satColor = Brushes.Red;
        public Brush SatColor {
            get => _satColor;
            set => SetProperty(ref _satColor, value);
        }

        private Brush _bladeColor = Brushes.Red;
        public Brush BladeColor {
            get => _bladeColor;
            set => SetProperty(ref _bladeColor, value);
        }

        private string _error;
        public string Error {
            get => _error;
            set => SetProperty(ref _error, value);
        }

        private string _latitude;
        public string Latitude {
            get => _latitude;
            set {
                SetProperty(ref _latitude, value);
                RaisePropertyChanged(nameof(Position));
            }
        }

        private string _longitude;
        public string Longitude {
            get => _longitude;
            set {
                SetProperty(ref _longitude, value);
                RaisePropertyChanged(nameof(Position));
            }
        }

        private string _altitude;
        public string Altitude {
            get => _altitude;
            set => SetProperty(ref _altitude, value);
        }

        private string _maxAltitude;
        public string MaxAltitude {
            get => _maxAltitude;
            set => SetProperty(ref _maxAltitude, value);
        }

        private string _heading;
        public string Heading {
            get => _heading;
            set => SetProperty(ref _heading, value);
        }

        private string _losDistance;
        public string LOSDistance {
            get => _losDistance;
            set => SetProperty(ref _losDistance, value);
        }

        private string _temperature;
        public string Temperature {
            get => _temperature;
            set => SetProperty(ref _temperature, value);
        }

        private string _pitchAngle;
        public string PitchAngle {
            get => _pitchAngle;
            set => SetProperty(ref _pitchAngle, value);
        }

        private string _rollAngle;
        public string RollAngle {
            get => _rollAngle;
            set => SetProperty(ref _rollAngle, value);
        }

        private double _batteryWidth;
        public double BatteryWidth {
            get => _batteryWidth;
            set => SetProperty(ref _batteryWidth, value);
        }

        private string _battery;
        public string Battery {
            get => _battery;
            set => SetProperty(ref _battery, value);
        }

        private string _selectedPort;
        public string SelectedPort {
            get => _selectedPort;
            set => SetProperty(ref _selectedPort, value);
        }

        private string _flightTimer;
        public string FlightTimer {
            get => _flightTimer;
            set => SetProperty(ref _flightTimer, value);
        }

        private bool _wayPointVisibility;
        public bool WayPointVisibility {
            get => _wayPointVisibility;
            set => SetProperty(ref _wayPointVisibility, value);
        }

        private bool _signalMessageVisibility;
        public bool SignalMessageVisibility {
            get => _signalMessageVisibility;
            set => SetProperty(ref _signalMessageVisibility, value);
        }

        private string _signalMessage;
        public string SignalMessage {
            get => _signalMessage;
            set => SetProperty(ref _signalMessage, value);
        }

        private Brush _batteryColor;
        public Brush BatteryColor {
            get => _batteryColor;
            set => SetProperty(ref _batteryColor, value);
        }

        private GMapProvider _selectedMap;
        public GMapProvider SelectedMap {
            get => _selectedMap;
            set => SetProperty(ref _selectedMap, value);
        }

        private PointLatLng _position;
        public PointLatLng Position {
            get {
                if (Latitude is null || Longitude is null)
                    return new PointLatLng(4.82241, 7.06130);
                if (CurrentMarker != null)
                    Map.Markers.Remove(CurrentMarker);

                var position = new PointLatLng(double.Parse(Latitude), double.Parse(Longitude));
                CurrentMarker = new GMapMarker(position) {
                    Offset = new Point(-15, -15),
                    ZIndex = int.MaxValue,
                };
                CurrentMarker.Shape = new RedMarker(CurrentMarker, $"Lat={Latitude},Long={Longitude}");
                Map.Markers.Add(CurrentMarker);
                return position;
                
            }

            set => SetProperty(ref _position, value);
        }

        private PointLatLng _homePosition;
        public PointLatLng HomePosition {
            get {
                return new PointLatLng(home_lat_gps, home_lon_gps);
            }
            set => SetProperty(ref _homePosition, value);
        }

        private bool _canShowHomeMarker;
        public bool CanShowHomeMarker {
            get => _canShowHomeMarker;
            set {
                _canShowHomeMarker = true;
                RaisePropertyChanged();
                ShowHomeMarker();
            }
        }

        #endregion


        public DelegateCommand OpenCloseCommand { get; }
        public DelegateCommand CreateListCommand { get; }
        public DelegateCommand FlyListCommand { get; }
        public DelegateCommand ResetCommand { get; }
        public string[] Ports { get; set; }
        public List<GMapProvider> Maps { get; }
        public GMapControl Map { get; set; }
        public GMapMarker CurrentMarker { get; set; }

        private SerialPort _serialPort;
        private byte check_byte;
        private byte temp_byte;
        private byte start;
        private byte first_receive;
        private byte received_data;
        private byte[] receive_buffer = new byte[50];
        private byte[] send_buffer = new byte[20];
        private int time_counter;
        private int receive_buffer_counter;
        private int receive_start_detect;
        private byte receive_byte_previous;
        private long milliseconds;
        private long last_receive;
        private double ground_distance;
        private double los_distance;
        public int error, flight_mode, roll_angle, pitch_angle;
        public int altitude_meters,
            max_altitude_meters,
            takeoff_throttle,
            actual_compass_heading,
            heading_lock,
            number_used_sats,
            fix_type,
            l_lat_gps,
            l_lon_gps,
            home_lat_gps,
            home_lon_gps;
        public int zoom = 17;
        public short temperature;
        public float battery_voltage,
          adjustable_setting_1,
          adjustable_setting_2,
          adjustable_setting_3;
        public int battery_bar_level;
        public int waypoint_list_counter, send_telemetry_data_counter, waypoint_send_step;
        public int click_lat, click_lon;
        public int[] waypoint_click_lat = new int[10];
        public int[] waypoint_click_lon = new int[10];
        public int new_telemetry_data_to_send;
        private Timer fly_waypoint_list;
        private Timer Send_telemetry_data;
        private byte home_gps_set;
        public int flight_timer_seconds;
        public bool create_waypoint_list;
        private Timer _alwaysRunningTimer;
        private Timer _flight_timer;
        private GMapMarker _marker;
        private GMapMarker _homeMarker;
        private IProgress<bool> _dataProgress;
    }
}
