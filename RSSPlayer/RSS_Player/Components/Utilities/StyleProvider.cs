using System;

namespace RssPlayer.Components.Utilities
{
    public class StyleProvider
    {
        public string GetCommonStyles()
        {
            try
            {
                return @"<style>
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, 'Open Sans', 'Helvetica Neue', sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
            background-color: #f0f4f8;
            color: #2d3748;
            text-align: center;
            padding: 20px;
            box-sizing: border-box;
        }
        .container {
            background-color: white;
            border-radius: 10px;
            box-shadow: 0 10px 25px rgba(0,0,0,0.1);
            padding: 40px;
            max-width: 600px;
            width: 100%;
        }
        .mac-address {
            background-color: #e6f2ff;
            border-radius: 5px;
            padding: 10px;
            margin-bottom: 20px;
            font-family: monospace;
            word-break: break-all;
        }
        .form-group {
            margin-bottom: 15px;
            text-align: left;
        }
        label {
            display: block;
            margin-bottom: 5px;
            color: #4a5568;
        }
        input, select {
            width: 100%;
            padding: 12px;
            border: 1px solid #cbd5e0;
            border-radius: 4px;
            box-sizing: border-box;
            font-size: 16px;
        }
        .btn {
            width: 100%;
            padding: 12px;
            background-color: #3182ce;
            color: white;
            border: none;
            border-radius: 4px;
            cursor: pointer;
            transition: background-color 0.3s ease;
            font-size: 16px;
            font-weight: 600;
            margin-top: 10px;
        }
        .btn:hover {
            background-color: #2c5282;
        }
        #status-message {
            margin-top: 15px;
            padding: 10px;
            border-radius: 4px;
            min-height: 20px;
        }
        .success {
            background-color: #9ae6b4;
            color: #22543d;
        }
        .error {
            background-color: #feb2b2;
            color: #742a2a;
        }
    </style>";
            }
            catch
            {
                return @"<style>
            body { font-family: sans-serif; }
        </style>";
            }
        }
        public string GetRegistrationStyles()
        {
            try
            {
                return @"<style>
        h1 {
            color: #3182ce;
            margin-bottom: 20px;
        }
        #loading {
            display: none;
            margin-top: 15px;
        }
    </style>";
            }
            catch
            {
                return @"<style>
            h1 { color: blue; }
        </style>";
            }
        }
        public string GetOfflineStyles()
        {
            try
            {
                return @"<style>
        body {
            background-color: #f7f7f7;
        }
        .container {
            text-align: center;
            padding: 2rem;
            background-color: white;
            border-radius: 8px;
            box-shadow: 0 4px 12px rgba(0,0,0,0.1);
            max-width: 600px;
            width: 90%;
        }
        .icon {
            font-size: 72px;
            margin-bottom: 1rem;
            color: #f44336;
        }
        h1 {
            margin: 0 0 1rem 0;
            color: #444;
        }
        .spinner {
            margin: 2rem auto;
            width: 64px;
            height: 64px;
            border: 8px solid #f3f3f3;
            border-top: 8px solid #3498db;
            border-radius: 50%;
            animation: spin 1.5s linear infinite;
        }
        .status {
            margin-top: 1rem;
            font-size: 14px;
            color: #666;
        }
        @keyframes spin {
            0% { transform: rotate(0deg); }
            100% { transform: rotate(360deg); }
        }
        .retry-button {
            margin-top: 1rem;
            background-color: #4CAF50;
            color: white;
            border: none;
            padding: 10px 20px;
            border-radius: 4px;
            cursor: pointer;
            font-size: 16px;
            transition: background-color 0.3s;
        }
        .retry-button:hover {
            background-color: #45a049;
        }
        .attempt-count {
            margin-top: 1rem;
            color: #888;
            font-size: 14px;
        }
    </style>";
            }
            catch
            {
                return @"<style>
            body { background-color: #f0f0f0; }
            .container { background-color: white; }
        </style>";
            }
        }
    }
}