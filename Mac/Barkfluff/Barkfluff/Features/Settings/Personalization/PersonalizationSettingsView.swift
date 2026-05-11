//
//  PersonalizationSettingsView.swift
//  Barkfluff
//
//  Раздел «Персонализация» в настройках. Три блока:
//  1. Постер профиля (превью + кнопка смены).
//  2. Закругление пузырей (превью + слайдер).
//  3. Фон чата (тогл блюра, слайдеры, сетка фонов).
//

import SwiftUI
import BFCore

struct PersonalizationSettingsView: View {
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: PersonalizationSettingsViewModel?

    @AppStorage("folders.compact") private var compactFolders: Bool = false
    @AppStorage("folders.excludeFromAll") private var excludeFolderChatsFromAll: Bool = false

    var body: some View {
        ScrollView {
            VStack(spacing: Theme.Spacing.lg) {
                if let viewModel {
                    content(viewModel: viewModel)
                } else {
                    ProgressView()
                        .frame(maxWidth: .infinity, minHeight: 200)
                }
            }
            .padding(Theme.Spacing.lg)
        }
        .frame(minWidth: 540, minHeight: 440)
        .task {
            if viewModel == nil {
                let vm = PersonalizationSettingsViewModel(
                    userService: container.userService,
                    fileService: container.fileService,
                    container: container
                )
                viewModel = vm
                await vm.load()
            }
        }
    }

    @ViewBuilder
    private func content(viewModel: PersonalizationSettingsViewModel) -> some View {
        @Bindable var vm = viewModel
        @Bindable var settings = container.personalizationSettings

        // Ошибки
        if let error = vm.errorMessage {
            HStack {
                Image(systemName: "exclamationmark.triangle.fill")
                    .foregroundStyle(.red)
                Text(error)
                    .font(.callout)
                    .foregroundStyle(.red)
                    .lineLimit(3)
                Spacer()
                Button("Скрыть") { vm.errorMessage = nil }
                    .buttonStyle(.borderless)
            }
            .padding(Theme.Spacing.md)
            .background(Color.red.opacity(0.08))
            .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.md, style: .continuous))
        }

        // Все блоки внутри одной Form — чтобы постер был такой же ширины,
        // как остальные секции (Form.grouped задаёт собственные insets).
        Form {
            Section {
                PosterPreviewCard(viewModel: vm)
                    .listRowInsets(EdgeInsets())
                    .listRowBackground(Color.clear)
            }

            Section("Внешний вид сообщений") {
                BubblePreviewView(cornerRadius: settings.bubbleCornerRadius)
                    .listRowInsets(EdgeInsets(top: 8, leading: 0, bottom: 8, trailing: 0))

                HStack {
                    Text("Закругление")
                    Slider(
                        value: Binding(
                            get: { Double(settings.bubbleCornerRadius) },
                            set: { settings.bubbleCornerRadius = Int($0.rounded()) }
                        ),
                        in: 0...30,
                        step: 1
                    )
                    Text("\(settings.bubbleCornerRadius) pt")
                        .font(.callout.monospacedDigit())
                        .foregroundStyle(.secondary)
                        .frame(width: 56, alignment: .trailing)
                }
            }

            Section("Фон чата") {
                Toggle("Размытие", isOn: $settings.backgroundBlurEnabled)

                if settings.backgroundBlurEnabled {
                    HStack {
                        Text("Радиус размытия")
                        Slider(
                            value: Binding(
                                get: { Double(settings.backgroundBlurRadius) },
                                set: { settings.backgroundBlurRadius = Int($0.rounded()) }
                            ),
                            in: 1...25,
                            step: 1
                        )
                        Text("\(settings.backgroundBlurRadius)")
                            .font(.callout.monospacedDigit())
                            .foregroundStyle(.secondary)
                            .frame(width: 56, alignment: .trailing)
                    }
                }

                HStack {
                    Text("Затемнение")
                    Slider(
                        value: Binding(
                            get: { Double(settings.backgroundDimPercent) },
                            set: { settings.backgroundDimPercent = Int($0.rounded()) }
                        ),
                        in: 0...100,
                        step: 1
                    )
                    Text("\(settings.backgroundDimPercent)%")
                        .font(.callout.monospacedDigit())
                        .foregroundStyle(.secondary)
                        .frame(width: 56, alignment: .trailing)
                }
            }

            Section("Папки чатов") {
                Toggle("Компактные папки", isOn: $compactFolders)
                Toggle("Не показывать чаты папок во «Все чаты»", isOn: $excludeFolderChatsFromAll)
            }

            Section {
                if vm.isLoading {
                    HStack {
                        Spacer()
                        ProgressView()
                        Spacer()
                    }
                    .padding(.vertical, Theme.Spacing.md)
                } else {
                    BackgroundsGrid(viewModel: vm, settings: settings)
                }
            } header: {
                HStack {
                    Text("Изображения фона")
                    Spacer()
                    if vm.deleteMode {
                        Button("Готово") { vm.deleteMode = false }
                            .buttonStyle(.borderless)
                            .controlSize(.small)
                    }
                }
            }
        }
        .formStyle(.grouped)
    }
}

#Preview {
    PersonalizationSettingsView()
        .environment(DependencyContainer())
}
