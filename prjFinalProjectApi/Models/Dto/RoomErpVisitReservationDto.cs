namespace prjFinalProjectApi.Models.Dto
{
    public class RoomErpVisitReservationDto
    {
        public int fReservationId { get; set; }
        public string fName { get; set; } = null!;
        public string fEmail { get; set; } = null!;
        public string fPhoneOrLineId { get; set; } = null!;
        public string fReservationDate { get; set; } // 字符串格式
        public string fCreatedAt { get; set; } // 字符串格式
        public bool fStatus { get; set; } // 對應資料庫的 fStatus
    }
}
