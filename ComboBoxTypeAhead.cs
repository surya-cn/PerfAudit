using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace PerfAudit
{
    public class ComboBoxTypeAhead
    {
        private readonly ComboBox _comboBox;
        private string _buffer = string.Empty;
        private long _lastTypingTimestamp = 0;

        public static ComboBoxTypeAhead Attach(ComboBox comboBox)
        {
            return new ComboBoxTypeAhead(comboBox);
        }

        public ComboBoxTypeAhead(ComboBox comboBox)
        {
            _comboBox = comboBox ?? throw new ArgumentNullException(nameof(comboBox));
            _comboBox.KeyDown += ComboBox_KeyDown;
            _comboBox.KeyPress += ComboBox_KeyPress;
            _comboBox.LostFocus += (s, e) => ClearBuffer();
        }

        public void ClearBuffer()
        {
            _buffer = string.Empty;
        }

        private void CheckTimeout()
        {
            long now = Stopwatch.GetTimestamp();
            double elapsed = (double)(now - _lastTypingTimestamp) / Stopwatch.Frequency;
            if (elapsed > 1.5)
            {
                _buffer = string.Empty;
            }
        }

        private void ComboBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                ClearBuffer();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Back)
            {
                CheckTimeout();
                _lastTypingTimestamp = Stopwatch.GetTimestamp();

                if (_buffer.Length > 0)
                {
                    _buffer = _buffer.Substring(0, _buffer.Length - 1);
                    if (_buffer.Length > 0)
                    {
                        int match = FindFirstMatch(_buffer);
                        if (match >= 0)
                        {
                            _comboBox.SelectedIndex = match;
                        }
                    }
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void ComboBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar))
            {
                CheckTimeout();
                _lastTypingTimestamp = Stopwatch.GetTimestamp();

                string candidate = _buffer + e.KeyChar;
                int match = FindFirstMatch(candidate);
                if (match >= 0)
                {
                    _buffer = candidate;
                    _comboBox.SelectedIndex = match;
                }
                e.Handled = true;
            }
        }

        private int FindFirstMatch(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return -1;
            for (int i = 0; i < _comboBox.Items.Count; i++)
            {
                string text = _comboBox.GetItemText(_comboBox.Items[i]);
                if (!string.IsNullOrEmpty(text) && text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
