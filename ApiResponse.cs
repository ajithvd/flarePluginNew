using System.Collections.Generic;

namespace AILinguistic
{
    public class ApiResponse
    {
        public List<SuggestionItem> Data { get; set; }
    }

    public class SuggestionItem
    {
        public string Original { get; set; }
        public string Corrected { get; set; }
    }
}
