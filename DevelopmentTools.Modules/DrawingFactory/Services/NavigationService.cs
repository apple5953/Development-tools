using System;
using System.Collections.Generic;
using BIMDrawingFactory.UI.Models;

namespace BIMDrawingFactory.UI.Services
{
    public interface INavigationService
    {
        NavigationPageKey CurrentPage { get; }
        event Action<NavigationPageKey, object> PageChanged;
        void NavigateTo(NavigationPageKey pageKey, object parameter = null);
        void GoBack();
    }

    public class NavigationService : INavigationService
    {
        private NavigationPageKey _currentPage = NavigationPageKey.QuickGenerate;
        private readonly Stack<KeyValuePair<NavigationPageKey, object>> _history = new Stack<KeyValuePair<NavigationPageKey, object>>();

        public NavigationPageKey CurrentPage
        {
            get { return _currentPage; }
        }

        public event Action<NavigationPageKey, object> PageChanged;

        public void NavigateTo(NavigationPageKey pageKey, object parameter = null)
        {
            if (_currentPage != pageKey)
            {
                _history.Push(new KeyValuePair<NavigationPageKey, object>(_currentPage, null));
            }
            _currentPage = pageKey;
            if (PageChanged != null)
            {
                PageChanged(pageKey, parameter);
            }
        }

        public void GoBack()
        {
            if (_history.Count > 0)
            {
                var prev = _history.Pop();
                _currentPage = prev.Key;
                if (PageChanged != null)
                {
                    PageChanged(prev.Key, prev.Value);
                }
            }
            else
            {
                NavigateTo(NavigationPageKey.QuickGenerate);
            }
        }
    }
}
