namespace BitbucketRepoBulkCloner.Models
{
    /// <summary>
    /// Basit repository modeli
    /// </summary>
    public class BitbucketRepo
    {
        public string Name { get; set; } = "";
        public string Slug { get; set; } = "";
        public string? CloneUrl { get; set; } = null;
    }
}
