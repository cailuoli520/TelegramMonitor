namespace TelegramMonitor;

public class SystemCacheServices : ISingleton
{
    private const string KEYWORDS_KEY = "telegramMonitor_keywords";
    private const string AD_KEY = "telegramMonitor_advertisement";

    private readonly IMemoryCache _cache;
    private readonly ISqlSugarClient _db;

    public SystemCacheServices(IMemoryCache cache, ISqlSugarClient db)
    {
        _cache = cache;
        _db = db;
    }

    private async Task<List<KeywordConfig>> SyncKeywordsFromDatabaseAsync()
    {
        var keywords = await _db.Queryable<KeywordConfig>().ToListAsync();
        _cache.Set(KEYWORDS_KEY, keywords, TimeSpan.FromDays(1));
        return keywords;
    }

    public Task<List<KeywordConfig>> GetKeywordsAsync() =>
        _cache.GetOrCreateAsync(KEYWORDS_KEY, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
            return await _db.Queryable<KeywordConfig>().ToListAsync();
        });

    public async Task AddKeywordsAsync(KeywordConfig keyword)
    {
        var list = await GetKeywordsAsync();
        if (list.Any(k => k.KeywordType == keyword.KeywordType &&
                          k.KeywordContent.Equals(keyword.KeywordContent, StringComparison.OrdinalIgnoreCase)))
            throw Oops.Oh("关键词已存在，请勿重复添加");

        keyword.Id = await _db.Insertable(keyword).ExecuteReturnIdentityAsync();
        list.Add(keyword);
        _cache.Set(KEYWORDS_KEY, list, TimeSpan.FromDays(1));
    }

    public async Task BatchAddKeywordsAsync(List<KeywordConfig> keywords)
    {
        var list = await GetKeywordsAsync();

        var toAdd = keywords
            .Where(k => !list.Any(e => e.KeywordType == k.KeywordType &&
                                       e.KeywordContent.Equals(k.KeywordContent, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (toAdd.Count == 0) throw Oops.Oh("所有关键词已存在，无需重复添加");

        await _db.Insertable(toAdd).ExecuteCommandAsync();
        list.AddRange(toAdd);
        _cache.Set(KEYWORDS_KEY, list, TimeSpan.FromDays(1));
        await SyncKeywordsFromDatabaseAsync();
    }

    public async Task UpdateKeywordsAsync(KeywordConfig keyword)
    {
        var list = await GetKeywordsAsync();
        if (list.Any(k => k.Id != keyword.Id &&
                          k.KeywordType == keyword.KeywordType &&
                          k.KeywordContent.Equals(keyword.KeywordContent, StringComparison.OrdinalIgnoreCase)))
            throw Oops.Oh("关键词已存在，请修改后重试");

        await _db.Updateable(keyword).ExecuteCommandAsync();

        var idx = list.FindIndex(k => k.Id == keyword.Id);
        if (idx >= 0) list[idx] = keyword;
        _cache.Set(KEYWORDS_KEY, list, TimeSpan.FromDays(1));
        await SyncKeywordsFromDatabaseAsync();
    }

    public async Task DeleteKeywordsAsync(int id)
    {
        await _db.Deleteable<KeywordConfig>().In(id).ExecuteCommandAsync();

        var list = await GetKeywordsAsync();
        list.RemoveAll(k => k.Id == id);
        _cache.Set(KEYWORDS_KEY, list, TimeSpan.FromDays(1));
        await SyncKeywordsFromDatabaseAsync();
    }

    public async Task BatchDeleteKeywordsAsync(IEnumerable<int> ids)
    {
        var idArr = ids.ToArray();
        await _db.Deleteable<KeywordConfig>().In(idArr).ExecuteCommandAsync();

        var list = await GetKeywordsAsync();
        list.RemoveAll(k => idArr.Contains(k.Id));
        _cache.Set(KEYWORDS_KEY, list, TimeSpan.FromDays(1));
        await SyncKeywordsFromDatabaseAsync();
    }

    public void SetAdvertisement(List<string> ads) =>
        _cache.Set(AD_KEY, ads, TimeSpan.FromHours(1));

    public string GetAdvertisement() =>
        (_cache.Get<List<string>>(AD_KEY) is { Count: > 0 } ads)
            ? ads[Random.Shared.Next(ads.Count)]
            : string.Empty;
}