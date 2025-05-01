namespace AbxClient.Models
{
    public class Packet
    {
        public string Symbol { get; set; }
        public char Side { get; set; }
        public int Quantity { get; set; }
        public int Price { get; set; }
        public int Seq { get; set; }
    }
}
