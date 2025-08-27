// prjFinalProjectApi/Models/RoomTable.cs
using System;
using System.Collections.Generic;

namespace prjFinalProjectApi.Models
{
    public class RoomTable
    {
        public int FRoomId { get; set; }
        public string? FRoomName { get; set; }
        public string? FRoomAlias { get; set; }
        public string? FRoomDescription { get; set; }
        public int? FRoomPrice { get; set; }
        public bool? FRoomType { get; set; }
        public int? FBedCount { get; set; }
        public string? FRoomStatus { get; set; }
        public DateTime? LastUpdated { get; set; }
        public virtual ICollection<RoomBed> RoomBeds { get; set; }
        public virtual ICollection<RoomImage> RoomImages { get; set; }

        public RoomTable()
        {
            RoomBeds = new HashSet<RoomBed>();
            RoomImages = new HashSet<RoomImage>();
        }
    }
}