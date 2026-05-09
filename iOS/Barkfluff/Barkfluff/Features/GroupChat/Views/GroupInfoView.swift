//
//  GroupInfoView.swift
//  Barkfluff (iOS)
//
//  Информация о групповом чате — переиспользует UserProfilePanelView для группы.
//

import SwiftUI
import BFCore

struct GroupInfoView: View {
    let chat: Chat

    var body: some View {
        UserProfilePanelView(chat: chat)
    }
}
