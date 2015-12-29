﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Mvc;
using Microsoft.Data.Entity;
using Microsoft.Extensions.Logging;
using ZBlog.Filters;
using ZBlog.Models;

// For more information on enabling MVC for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace ZBlog.Controllers
{
    public class PostController : Controller
    {
        private readonly ZBlogDbContext _dbContext;
        private readonly ILogger _logger;

        public PostController(ZBlogDbContext dbContext, ILoggerFactory loggerFactory)
        {
            _dbContext = dbContext;
            _logger = loggerFactory.CreateLogger<PostController>();
        }

        // GET: /Post/Index
        [AdminRequired]
        public async Task<IActionResult> Index(ManageMessageId? message = null)
        {
            ViewData["StatusMessage"] =
                message == ManageMessageId.PostNewSuccess ? "Your post has been posted."
                : message == ManageMessageId.PostEditSuccess ? "Your post has been updated."
                : message == ManageMessageId.PostDeleteSuccess ? "Your post has been deleted."
                : message == ManageMessageId.Error ? "An error has occurred."
                : "";

            var posts =
                await
                    _dbContext.Posts.Include(p => p.Catalog)
                        .Include(p => p.PostTags)
                        .ThenInclude(p => p.Tag)
                        .Include(p => p.User)
                        .OrderByDescending(p => p.CreateTime)
                        .ToListAsync();

            return View(posts);
        }

        // GET: /Post/New
        [AdminRequired]
        public async Task<IActionResult> New()
        {
            ViewBag.Catalogs = await _dbContext.Catalogs.OrderByDescending(c => c.PRI).ToListAsync();
            return View();
        }

        // POST: /Post/New
        [HttpPost]
        [AdminRequired]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> New(Post model, string tags, CancellationToken requestAborted)
        {
            if (ModelState.IsValid)
            {
                model.CreateTime = model.LastEditTime = DateTime.Now;
                model.Visits = 0;
                var length = model.Content.Length;
                model.Summary = model.Content.Substring(0, length >= 100 ? 100 : length);
                model.User = await GetCurrentUserAsync();
                _dbContext.Add(model);
                await _dbContext.SaveChangesAsync(requestAborted);
                if (!string.IsNullOrWhiteSpace(tags))
                {
                    await UpdatePostTags(model, tags, requestAborted);
                }
                _logger.LogDebug($"New post<{model.Title}>.");
                return RedirectToAction(nameof(Index), new { Message = ManageMessageId.PostNewSuccess });
            }
            var catalog = await _dbContext.Catalogs.OrderByDescending(c => c.PRI).ToListAsync(requestAborted);
            ViewBag.Catalogs = catalog;
            return View(model);
        }

        // GET: /Post/Edit/id
        [AdminRequired]
        public async Task<IActionResult> Edit(int? id)
        {
            _logger.LogDebug($"Edit post<{id}>");

            if (!id.HasValue)
            {
                return HttpNotFound();
            }

            var post = await _dbContext.Posts.Include(p => p.Catalog)
                .Include(p => p.PostTags)
                .ThenInclude(p => p.Tag)
                .Include(p => p.User).SingleOrDefaultAsync(p => p.Id == id);

            if (post == null)
            {
                return HttpNotFound();
            }

            ViewData["Title"] = $"Post Edit<{post.Title}>";
            
            ViewBag.Catalogs = await _dbContext.Catalogs.OrderByDescending(c => c.PRI).ToListAsync();

            return View(post);
        }

        // POST: /Post/Edit/id
        [HttpPost]
        [AdminRequired]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Post model, string tags, CancellationToken requestAborted)
        {
            if (ModelState.IsValid)
            {
                model.LastEditTime = DateTime.Now;
                var length = model.Content.Length;
                model.Summary = model.Content.Substring(0, length >= 100 ? 100 : length);
                model.User = await GetCurrentUserAsync();
                _dbContext.Update(model);
                await _dbContext.SaveChangesAsync(requestAborted);
                
                await UpdatePostTags(model, tags, requestAborted);
                
                _logger.LogDebug($"Updated post<{model.Title}>");

                return RedirectToAction(nameof(Index), new { Message = ManageMessageId.PostEditSuccess });
            }
            
            ViewBag.Catalogs = await _dbContext.Catalogs.OrderByDescending(c => c.PRI).ToListAsync(requestAborted);

            return View(model);
        }

        // GET: /Post/Delete/id
        [AdminRequired]
        public async Task<IActionResult> Delete(int? id)
        {
            _logger.LogDebug($"Delete post<{id}>");

            if (!id.HasValue)
            {
                return HttpNotFound();
            }

            var post = await _dbContext.Posts.SingleOrDefaultAsync(p => p.Id == id);

            if (post == null)
            {
                return HttpNotFound();
            }

            ViewData["Title"] = $"Post Delete<{post.Title}> Confirmation";
            
            return View(post);
        }

        // POST: /Post/Delete/id
        [HttpPost]
        [AdminRequired]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, CancellationToken requestAborted)
        {
            var post = await _dbContext.Posts.Include(p => p.PostTags)
                        .OrderByDescending(p => p.CreateTime)
                        .SingleOrDefaultAsync(p => p.Id == id, requestAborted);

            _dbContext.Remove(post);
            foreach (var postTag in post.PostTags)
            {
                //when count of the tag' posts less 1, then delete it.
                var tag = await _dbContext.Tags.Include(t => t.PostTags).SingleOrDefaultAsync(p => p.Id == postTag.TagId, requestAborted);
                if (tag.PostTags.Count <= 1)
                {
                    _dbContext.Remove(tag);
                }
                _dbContext.Remove(postTag);
            }
            
            await _dbContext.SaveChangesAsync(requestAborted);

            _logger.LogDebug($"Deleted post<{post.Title}>");

            return RedirectToAction(nameof(Index), new { Message = ManageMessageId.PostDeleteSuccess });
        }

        private async Task UpdatePostTags(Post model, string tags, CancellationToken requestAborted)
        {
            var postTags = _dbContext.PostTags.Where(p => p.PostId == model.Id);
            _logger.LogDebug($"Remove posttags count:{postTags.Count()}");
            _dbContext.PostTags.RemoveRange(postTags);
            await _dbContext.SaveChangesAsync(requestAborted);

            var tagArray = tags.Split(new[] {',', '，', ' '}, StringSplitOptions.RemoveEmptyEntries);
            foreach (var t in tagArray)
            {
                var postTag = new PostTag
                {
                    PostId = model.Id
                };
                var tag =
                    await _dbContext.Tags.FirstOrDefaultAsync(
                        x => x.Name.Equals(t, StringComparison.OrdinalIgnoreCase), requestAborted);
                if (tag == null)
                {
                    tag = new Tag
                    {
                        Name = t
                    };
                    _dbContext.Add(tag);
                    await _dbContext.SaveChangesAsync(requestAborted);
                }
                postTag.TagId = tag.Id;
                _dbContext.Add(postTag);
            }
            await _dbContext.SaveChangesAsync(requestAborted);
        }

        #region Helpers

        public enum ManageMessageId
        {
            PostNewSuccess,
            PostEditSuccess,
            PostDeleteSuccess,
            Error
        }

        private async Task<User> GetCurrentUserAsync()
        {
            return await _dbContext.Users.FirstOrDefaultAsync(u => u.Name.Equals(HttpContext.Session.GetString("AdminName")));
        }

        private IActionResult RedirectToLocal(string returnUrl)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            else
            {
                return RedirectToAction(nameof(HomeController.Index), "Home");
            }
        }

        #endregion
    }
}