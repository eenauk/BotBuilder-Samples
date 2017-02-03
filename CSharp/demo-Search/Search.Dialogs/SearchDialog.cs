namespace Search.Dialogs
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Bot.Builder.Dialogs;
    using Microsoft.Bot.Builder.Internals.Fibers;
    using Microsoft.Bot.Connector;
    using Search.Models;
    using Search.Services;
    using AskLuis;

    [Serializable]
    public abstract class SearchDialog : IDialog<IList<SearchHit>>
    {
        protected readonly ISearchClient SearchClient;
        protected readonly SearchQueryBuilder QueryBuilder;
        protected readonly PromptStyler HitStyler;
        protected readonly bool MultipleSelection;
        private readonly IList<SearchHit> selected = new List<SearchHit>();

        private bool firstPrompt = true;
        private IList<SearchHit> found;

        private Luis luis = new Luis("ca543180a0714f28bf29312a667a510a", "330d4213-c738-4df2-8cf8-90a1f4992d4d");

        public SearchDialog(ISearchClient searchClient, SearchQueryBuilder queryBuilder = null, PromptStyler searchHitStyler = null, bool multipleSelection = false)
        {
            SetField.NotNull(out this.SearchClient, nameof(searchClient), searchClient);

            this.QueryBuilder = queryBuilder ?? new SearchQueryBuilder();
            this.HitStyler = searchHitStyler ?? new SearchHitStyler();
            this.MultipleSelection = multipleSelection;
        }

        public Task StartAsync(IDialogContext context)
        {
            return this.InitialPrompt(context);
        }

        public async Task Search(IDialogContext context, IAwaitable<string> input)
        {
            string text = input != null ? await input : null;
            await SearchOnString(context, text);
        }

            public async Task SearchOnString(IDialogContext context, string input)
        {
            string text = input;
            if (this.MultipleSelection && text != null && text.ToLowerInvariant() == "list")
            {
                await this.ListAddedSoFar(context);
                await this.InitialPrompt(context);
            }
            else
            {
                if (text != null)
                {
                    await askLuis(text);
                    //this.QueryBuilder.SearchText = text;
                }

                var response = await this.ExecuteSearchAsync();

                if (response.Results.Count() == 0)
                {
                    await this.NoResultsConfirmRetry(context);
                }
                else
                {
                    var message = context.MakeMessage();
                    this.found = response.Results.ToList();
                    this.HitStyler.Apply(
                        ref message,
                        "Here are a few good options I found:",
                        this.found.ToList().AsReadOnly());
                    await context.PostAsync(message);
                    await context.PostAsync(
                        this.MultipleSelection ?
                        "You can select one or more to add to your list, *list* what you've selected so far, *refine* these results, see *more* or search *again*." :
                        "You can select one, *refine* these results, see *more* or search *again*.");
                    context.Wait(this.ActOnSearchResults);
                }
            }
        }

        private async Task askLuis(string text)
        {
            LuisResponse response = await luis.GetIntent(text);

            string intent = response.topScoringIntent.intent;
            if (intent != "None")
            {
                //new lookup so clear everything out
                if (intent == "house lookup")
                {
                    this.QueryBuilder.Refinements.Clear();
                    this.QueryBuilder.PageNumber = 1;
                    this.QueryBuilder.SearchText = "";
                }
                //if either new lookup or refinement, then set as many refinements as possible
                foreach (var e in response.entities)
                {
                    switch (e.type)
                    {
                        case "bedrooms":
                            this.QueryBuilder.Refinements.Remove("beds");
                            this.QueryBuilder.Refinements.Add("beds", new List<string>() { e.entity });
                            break;
                        case "bathrooms":
                            this.QueryBuilder.Refinements.Remove("baths");
                            this.QueryBuilder.Refinements.Add("baths", new List<string>() { e.entity });
                            break;
                        case "builtin.geography.city":
                            this.QueryBuilder.Refinements.Remove("city");
                            this.QueryBuilder.Refinements.Add("city", new List<string>() { UppercaseFirstLetter(e.entity) });
                            break;
                        case "city":
                            this.QueryBuilder.Refinements.Remove("city");
                            this.QueryBuilder.Refinements.Add("city", new List<string>() { UppercaseFirstLetter(e.entity) });
                            break;
                        case "PriceBegin":
                            this.QueryBuilder.Refinements.Remove("MinPrice");
                            this.QueryBuilder.Refinements.Add("MinPrice", new List<string>() { CleanupPrice(e.entity) });
                            break;
                        case "PriceEnd":
                            this.QueryBuilder.Refinements.Remove("MaxPrice");
                            this.QueryBuilder.Refinements.Add("MaxPrice", new List<string>() { CleanupPrice(e.entity) });
                            break;
                    }
                }
            }
            else
            {
                this.QueryBuilder.SearchText = text; 
            }
            /*
            var beds = result.Document["beds"];
            var baths = result.Document["baths"];
            var city = result.Document["city"];
            var price = result.Document["price"];
            */
        }

        private string CleanupPrice(string dirtyPrice)
        {
            string cleanPrice = dirtyPrice;
            cleanPrice = cleanPrice.Replace("$", "");
            cleanPrice = cleanPrice.Replace(",", "");
            cleanPrice = cleanPrice.Replace("k", "000");
            cleanPrice = cleanPrice.Replace(" ","");

            return cleanPrice;
        }

        private string UppercaseFirstLetter(string text)
        {
            if (String.IsNullOrEmpty(text)) return String.Empty;
            return text.First().ToString().ToUpper() + text.Substring(1);
        }

        protected virtual Task InitialPrompt(IDialogContext context)
        {
            string prompt = "What would you like to search for?";

            if (!this.firstPrompt)
            {
                prompt = "What else would you like to search for?";
                if (this.MultipleSelection)
                {
                    prompt += " You can also *list* all items you've added so far.";
                }
            }

            this.firstPrompt = false;

            PromptDialog.Text(context, this.Search, prompt);
            return Task.CompletedTask;
        }

        protected virtual Task NoResultsConfirmRetry(IDialogContext context)
        {
            PromptDialog.Confirm(context, this.ShouldRetry, "Sorry, I didn't find any matches. Do you want to retry your search?");
            return Task.CompletedTask;
        }

        protected virtual async Task ListAddedSoFar(IDialogContext context)
        {
            var message = context.MakeMessage();
            if (this.selected.Count == 0)
            {
                await context.PostAsync("You have not added anything yet.");
            }
            else
            {
                this.HitStyler.Apply(ref message, "Here's what you've added to your list so far.", this.selected.ToList().AsReadOnly());
                await context.PostAsync(message);
            }
        }

        protected virtual async Task AddSelectedItem(IDialogContext context, string selection)
        {
            SearchHit hit = this.found.SingleOrDefault(h => h.Key == selection);
            if (hit == null)
            {
                await this.UnkownActionOnResults(context, selection);
            }
            else
            {
                if (!this.selected.Any(h => h.Key == hit.Key))
                {
                    this.selected.Add(hit);
                }

                if (this.MultipleSelection)
                {
                    await context.PostAsync($"'{hit.Title}' was added to your list!");
                    PromptDialog.Confirm(context, this.ShouldContinueSearching, "Do you want to continue searching and adding more items?");
                }
                else
                {
                    context.Done(this.selected);
                }
            }
        }

        protected virtual async Task UnkownActionOnResults(IDialogContext context, string action)
        {
            await context.PostAsync("Not sure what you mean. You can search *again*, *refine*, *list* or select one of the items above. Or are you *done*?");
            context.Wait(this.ActOnSearchResults);
        }

        protected virtual async Task ShouldContinueSearching(IDialogContext context, IAwaitable<bool> input)
        {
            try
            {
                bool shouldContinue = await input;
                if (shouldContinue)
                {
                    await this.InitialPrompt(context);
                }
                else
                {
                    context.Done(this.selected);
                }
            }
            catch (TooManyAttemptsException)
            {
                context.Done(this.selected);
            }
        }

        protected void SelectRefiner(IDialogContext context)
        {
            var dialog = new SearchSelectRefinerDialog(this.GetTopRefiners(), this.QueryBuilder);
            context.Call(dialog, this.Refine);
        }

        protected async Task Refine(IDialogContext context, IAwaitable<string> input)
        {
            string refiner = await input;

            if (!string.IsNullOrWhiteSpace(refiner))
            {
                var dialog = new SearchRefineDialog(this.SearchClient, refiner, this.QueryBuilder);
                context.Call(dialog, this.ResumeFromRefine);
            }
            else
            {
                await this.Search(context, null);
            }
        }

        protected async Task ResumeFromRefine(IDialogContext context, IAwaitable<string> input)
        {
            await input; // refiner filter is already applied to the SearchQueryBuilder instance we passed in
            await this.Search(context, null);
        }

        protected async Task<GenericSearchResult> ExecuteSearchAsync()
        {
            return await this.SearchClient.SearchAsync(this.QueryBuilder);
        }

        protected abstract string[] GetTopRefiners();

        private async Task ShouldRetry(IDialogContext context, IAwaitable<bool> input)
        {
            try
            {
                bool retry = await input;
                if (retry)
                {
                    await this.InitialPrompt(context);
                }
                else
                {
                    context.Done<IList<SearchHit>>(null);
                }
            }
            catch (TooManyAttemptsException)
            {
                context.Done<IList<SearchHit>>(null);
            }
        }

        private async Task ActOnSearchResults(IDialogContext context, IAwaitable<IMessageActivity> input)
        {
            var activity = await input;
            var choice = activity.Text;

            switch (choice.ToLowerInvariant())
            {
                case "again":
                case "reset":
                    this.QueryBuilder.Reset();
                    await this.InitialPrompt(context);
                    break;

                case "more":
                    this.QueryBuilder.PageNumber++;
                    await this.Search(context, null);
                    break;

                case "refine":
                    this.SelectRefiner(context);
                    break;

                case "list":
                    await this.ListAddedSoFar(context);
                    context.Wait(this.ActOnSearchResults);
                    break;

                case "done":
                    context.Done(this.selected);
                    break;

                default:
                    await this.SearchOnString(context, choice); 
                    break;
            }
        }
    }
}
