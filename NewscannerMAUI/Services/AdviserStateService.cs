using System;

namespace NewscannerMAUI.Services
{
    public class AdviserStateService
    {
        private int _pendingStudentsCount;
        public int PendingStudentsCount
        {
            get => _pendingStudentsCount;
            private set
            {
                if (_pendingStudentsCount != value)
                {
                    _pendingStudentsCount = value;
                    NotifyStateChanged();
                }
            }
        }

        public event Action? OnStateChanged;

        public void SetPendingCount(int count)
        {
            PendingStudentsCount = count;
        }

        public void DecrementPendingCount()
        {
            if (PendingStudentsCount > 0)
            {
                PendingStudentsCount--;
            }
        }

        private void NotifyStateChanged() => OnStateChanged?.Invoke();
    }
}
