namespace Domain.Common
{
    public class PagedList<T>
    {
        /// <summary>
        /// Gets or sets the items for the current page.
        /// </summary>
        public IEnumerable<T> Items { get; set; } = new List<T>();

        /// <summary>
        /// Gets or sets the total number of records.
        /// </summary>
        public int TotalRecords { get; set; }

        /// <summary>
        /// Gets or sets the current page that the items.
        /// </summary>
        public int Page { get; set; }

        /// <summary>
        /// Gets or sets the number of items to return per page.
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Gets the total number of pages.
        /// </summary>
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalRecords / (decimal)PageSize) : 0;

        /// <summary>
        /// Gets whether there is a previous page.
        /// </summary>
        public bool HasPrevious => Page > 1;

        /// <summary>
        /// Gets whether there is a next page.
        /// </summary>
        public bool HasNext => Page < TotalPages;
    }
}
