namespace prjFinalProjectApi.Models.Dto
{
    public class RoomDetailDto
    {
        public int FRoomId { get; set; }
        public string FRoomAlias { get; set; }
        public string[] Images { get; set; } // 所有圖片
        public string FRoomDescription { get; set; }
        public int? FRoomPrice { get; set; }
        public int? FBedCount { get; set; }
        public bool IsAvailable { get; set; } // 根據床位狀態計算
        public int AvailableBeds { get; set; } // 剩餘床位數
    }
}