# HandlerFinder

This is an extension for Visual Studio to help finding Command/Request Handlers for MediatR.

## Usage
- Right click on a type, class definition or record to open up the context menu
- Click "Find handler"
- If there is a handler, visual studio will navigate to it
- In case it was not able to find a handler it will not open anything & show a message in the status bar on the bottom left.
## Logic
- Iterate over all solution *.cs documents
- Find all method declarations named "Handle"
- If the type argument matches the request handlers name, it was successful
- The filename and the line position will be determined
- Visual studio opens the file and set the cursor to the line where the handler method is defined

## Notes
- It also works for records
- It also works for class definitions
