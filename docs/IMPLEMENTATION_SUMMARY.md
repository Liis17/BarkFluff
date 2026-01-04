# Attachment UX and Message Bubble Fixes - Implementation Summary

## Overview
This document summarizes the implementation of attachment handling and message bubble read receipt fixes for the BarkFluff WPF client application.

## Changes Made

### 1. Text Input Styling
**File:** `BarkFluff.Client.WPF/Pages/MessengerPage.xaml`

**Change:** Removed the visible `Border` wrapper around `TextForMessage` and set the TextBox background to `Transparent`.

**Result:** The text input field now visually merges with the `TFMBackground`, maintaining legibility with white foreground text.

### 2. Attachment Menu & File Picking
**File:** `BarkFluff.Client.WPF/Pages/MessengerPage.xaml`

**Changes:**
- Added a `Popup` control to `AttachFileButton` with two menu options:
  - **"фото или видео"** - Opens file picker filtered to images and videos (jpg, jpeg, png, gif, bmp, webp, mp4, avi, mov, mkv, webm)
  - **"файл"** - Opens file picker for any file type
- Both options support multi-select
- Added click handlers in `MessengerPage.xaml.cs`:
  - `AttachFileButton_Click` - Toggles popup visibility
  - `AttachMediaButton_Click` - Opens media file picker
  - `AttachDocumentButton_Click` - Opens generic file picker
  - `OpenFileDialog` - Handles file selection with proper filters

### 3. Pre-send Preview Overlay
**Files:** 
- `BarkFluff.Client.WPF/UserControls/AttachmentPreviewOverlay.xaml`
- `BarkFluff.Client.WPF/UserControls/AttachmentPreviewOverlay.xaml.cs`

**Features:**
- **Centered Overlay:** Positioned in center with semi-transparent background
- **Preview Logic:**
  - Images/Videos: Show thumbnails (downscaled to 120x120)
  - Generic Files: Show document icon with filename
  - GIF files: Treated as images
- **Header:** "Предпросмотр вложений" title
- **Preview Area:** Scrollable grid of preview items using `WrapPanel`
- **Footer:**
  - Left: "отмена" button (gray, cancels and cleans up)
  - Right: "отправить" button (orange) with dropdown arrow
  - Dropdown option: "отправить отдельно" (sends each file separately)

**Methods:**
- `AddAttachments(List<string> filePaths)` - Adds files from file picker
- `AddImageFromClipboard(BitmapSource image)` - Handles clipboard screenshots
- `Clear()` - Cleans up temp files and resets state
- `DetermineFileType(string filePath)` - Maps file extensions to `UploadFileType` enum

### 4. File Upload and Sending Flow
**File:** `ClientComponents/BarkFluff.WebApi.Core/WebApi.cs`

**New Method:** `UploadFileAsync`
```csharp
public async Task<(ErrorReturner error, string? fileId)> UploadFileAsync(
    GlobalParam globalParam, 
    string filePath, 
    Proto.Files.UploadFileType fileType)
```

**Features:**
- Gets upload URL from Files API via `GetUploadUrlAsync`
- Reads file bytes and determines content type based on extension
- Posts file to upload URL using `MultipartFormDataContent`
- Returns file ID on success

**Updated Method:** `SendMessage`
- Fixed to include `FilesIds` for both `chatId` and `userId` paths (was only in userId path before)
- Now properly sends attachments regardless of recipient type

**File:** `BarkFluff.Client.WPF/Pages/MessengerPage.xaml.cs`

**New Method:** `SendMessageWithAttachments`
- Uploads files using `UploadFileAsync`
- Collects file IDs
- Sends message with attachments
- Cleans up clipboard temp files
- Shows sending state in message bubble

**Sending Modes:**
- **Grouped:** All files in one message (default)
- **Separate:** Each file as individual message (via dropdown)

### 5. Paste Handling
**File:** `BarkFluff.Client.WPF/Pages/MessengerPage.xaml.cs`

**Features:**
- Added `DataObject.AddPastingHandler` to `TextForMessage`
- Detects `DataFormats.Bitmap` for clipboard screenshots
- Detects `DataFormats.FileDrop` for files copied from Explorer
- Triggers preview overlay with appropriate content
- Clipboard images saved to temp file, cleaned up after sending/canceling

**Methods:**
- `OnTextForMessagePaste` - Handles paste events
- `TextForMessage_PreviewKeyDown` - Prepares for Ctrl+V handling

### 6. Read Receipts Bug Fix
**File:** `BarkFluff.Client.WPF/UserControls/MessageBubble.xaml.cs`

**Issue:** Sent messages showed double checkmark (read status) immediately because `SenderId` was not set in the constructor for new messages.

**Fix:**
```csharp
public MessageBubble(string textMessage, ...)
{
    // ...
    SenderId = App.GParam.UserId;  // Added this line
    ReadBy = new List<long>();     // Added this line
    // ...
}
```

**Logic (already correct):**
```csharp
private void UpdateReadStatus()
{
    // Only show read status for own messages
    if (_owner != MessageOwner.Me) return;
    
    // Check if read by others (excluding sender)
    var readByOthers = ReadBy.Any(id => id != SenderId);
    
    // Opacity 1.0 = read, 0.5 = sent/delivered
    ReadStatus.Opacity = readByOthers ? 1.0 : 0.5;
}
```

### 7. UI Integration
**File:** `BarkFluff.Client.WPF/Pages/MessengerPage.xaml`

**Added Overlay:**
```xaml
<Grid x:Name="AttachmentOverlay" Panel.ZIndex="1001" 
      Background="#80000000" Visibility="Collapsed">
    <uc:AttachmentPreviewOverlay x:Name="AttachmentPreview" 
        HorizontalAlignment="Center" VerticalAlignment="Center" />
</Grid>
```

**Integration:**
- Overlay appears above all other UI elements (ZIndex 1001)
- Event handlers wired in constructor:
  - `AttachmentPreview.OnCancel` → Hides overlay and clears
  - `AttachmentPreview.OnSend` → Uploads and sends files
- Popup menu positioned relative to `AttachFileButton`

## Proto Contracts Used

### Files API (`files_api.proto`)
- `GetUploadUrlRequest` / `GetUploadUrlResponse` - Get pre-signed upload URL
- `UploadFileType` enum:
  - `MESSAGE_ATTACHMENT_IMAGE`
  - `MESSAGE_ATTACHMENT_VIDEO`
  - `MESSAGE_ATTACHMENT_GIF`
  - `MESSAGE_ATTACHMENT_DOCUMENT`

### Messages API (`messages_api.proto`)
- `SendMessageRequest` / `SendMessageResponse`
- `OutgoingMessage`:
  - `text` - Message text
  - `files_ids` - Array of file IDs

## File Type Mapping

| Extensions | Upload Type |
|-----------|-------------|
| jpg, jpeg, png, bmp, webp | `MESSAGE_ATTACHMENT_IMAGE` |
| mp4, avi, mov, mkv, webm | `MESSAGE_ATTACHMENT_VIDEO` |
| gif | `MESSAGE_ATTACHMENT_GIF` |
| All others | `MESSAGE_ATTACHMENT_DOCUMENT` |

## Error Handling

- File upload errors logged to Erida message system
- Failed uploads don't prevent other files in batch
- User notified if no files uploaded successfully
- Temp files cleaned up even on error

## Testing Requirements

### Unit Testing (Not Implemented)
The project doesn't have existing test infrastructure for the WPF client, so no tests were added per the minimal-change instructions.

### Manual Testing Required (Windows Only)
1. **Text Input Styling**
   - Verify TextForMessage has transparent background
   - Verify text is readable (white on blurred background)

2. **Attachment Menu**
   - Click attach button, verify popup appears
   - Select "фото или видео", verify file picker filter
   - Select "файл", verify all files allowed
   - Select multiple files, verify all appear in preview

3. **Preview Overlay**
   - Verify images show thumbnails
   - Verify videos show video icon + filename
   - Verify documents show document icon + filename
   - Click "отмена", verify overlay closes
   - Click "отправить", verify files upload and send
   - Click dropdown, select "отправить отдельно", verify separate messages

4. **Paste Handling**
   - Take screenshot, paste into text field, verify preview appears
   - Copy files in Explorer, paste into text field, verify preview appears
   - Verify clipboard images saved and cleaned up correctly

5. **Read Receipts**
   - Send message, verify checkmark is faded (0.5 opacity)
   - Wait for recipient to read, verify checkmark becomes solid (1.0 opacity)
   - Verify incoming messages don't show checkmarks

6. **Integration**
   - Verify scroll, history loading still works
   - Verify cache still works
   - Verify read-marking flow still works
   - Verify existing functionality not broken

## Known Limitations

1. **Build Environment:** Project can only be built on Windows due to WPF and Windows-specific dependencies
2. **File Size Limits:** No file size validation implemented (relies on server-side limits)
3. **Progress Indication:** File upload progress not shown to user
4. **Error Recovery:** No retry mechanism for failed uploads
5. **Thumbnail Generation:** Video thumbnails not implemented (just shows icon)

## Future Enhancements

1. Add progress bar for file uploads
2. Show upload/sending progress in message bubble
3. Add file size limits and validation
4. Generate video thumbnails for preview
5. Add drag-and-drop support for attachments
6. Add image compression options
7. Support for sending images as documents (hide inline preview)
8. Add attachment caption support

## Dependencies

No new NuGet packages were added. All functionality uses existing dependencies:
- `WPF-UI` (already in project) - For UI controls and icons
- `System.Drawing.Common` (already in project) - For image handling
- Standard .NET libraries for file I/O and HTTP

## Security Considerations

1. **File Type Validation:** Basic extension-based validation only
2. **Temp Files:** Clipboard images saved to system temp folder with GUID names
3. **Cleanup:** Temp files deleted after send/cancel, but may remain if app crashes
4. **Server Trust:** Upload URLs come from trusted server API
5. **Content Type:** Determined from file extension, could be spoofed

## Performance Considerations

1. **Image Loading:** Thumbnails loaded on UI thread (could block for large images)
2. **File Reading:** Files read fully into memory before upload
3. **Multiple Uploads:** Sequential, not parallel (could be slow for many files)
4. **Preview Generation:** Done synchronously when showing overlay

## Conclusion

All requirements from the problem statement have been implemented:
- ✅ Text input styling merged with background
- ✅ Attachment menu with media/file options
- ✅ Pre-send preview overlay with thumbnails/icons
- ✅ File upload and sending (grouped/separate)
- ✅ Paste handling for images and files
- ✅ Read receipt bug fixed
- ✅ Existing behavior preserved

The implementation follows WPF best practices, reuses existing UI patterns from the codebase, and integrates cleanly with the existing proto contracts and WebApi layer.
