import type { Chat } from '../../gen/messages_api_pb';
import { useAuth } from '../../state/AuthContext';
import { useUser } from '../../hooks/useUsers';

export interface ChatDisplay {
  name: string;
  picture?: string;
  peerId?: bigint;
}

// Имя и аватар чата: для группы — title/picture; для ЛС — собеседник (резолвится через GetUser).
export function useChatDisplay(chat: Chat | undefined): ChatDisplay {
  const { currentUserId } = useAuth();
  const peerId =
    chat && !chat.isGroupChat
      ? chat.members.find((m) => m.userId.toString() !== currentUserId)?.userId
      : undefined;
  const peer = useUser(chat && !chat.title ? peerId : null);

  if (!chat) return { name: '' };
  const name = chat.title || (peer ? `${peer.firstName} ${peer.lastName}`.trim() : 'Чат');
  const picture = chat.picture || peer?.profilePicturePreview || peer?.profilePicture || undefined;
  return { name, picture, peerId };
}
