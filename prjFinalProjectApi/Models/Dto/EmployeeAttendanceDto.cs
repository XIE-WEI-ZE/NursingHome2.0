namespace prjFinalProjectApi.Models.Dto
{
    public class EmployeeAttendanceDto
    {
        public int? AttendanceId { get; set; }
        public int EmployeeId { get; set; }
        public DateTime WorkDate { get; set; }
        public DateTime? ClockInTime { get; set; }
        public DateTime? ClockOutTime { get; set; }
        public string? Status { get; set; }
        public bool CanClockIn { get; set; }
        public bool CanClockOut { get; set; }
    }
}
