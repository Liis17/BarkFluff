//
//  BioStepView.swift
//  Barkfluff
//
//  Шаг 7: Описание профиля (iOS)
//

import SwiftUI
import BFCore

struct BioStepView: View {
    @Bindable var data: RegistrationData
    let userService: UserServiceProtocol

    let maxBioLength = 500

    var body: some View {
        VStack(spacing: 12) {
            // Текстовое поле
            VStack(alignment: .leading, spacing: 4) {
                Text("auth.register.step.bio.label")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                TextEditor(text: $data.bio)
                    .frame(height: 100)
                    .padding(8)
                    .background(Color(uiColor: .secondarySystemGroupedBackground))
                    .clipShape(RoundedRectangle(cornerRadius: 8))
                    .onChange(of: data.bio) { _, newValue in
                        if newValue.count > maxBioLength {
                            data.bio = String(newValue.prefix(maxBioLength))
                        }
                    }
            }

            HStack {
                Text("auth.register.step.bio.hint_short")
                    .font(.caption)
                    .foregroundStyle(.secondary)

                Spacer()

                Text(verbatim: "\(data.bio.count)/\(maxBioLength)")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            // Подсказка
            Text("auth.register.step.bio.hint")
                .font(.caption)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
    }
}

#Preview {
    BioStepView(data: RegistrationData(), userService: DependencyContainer().userService)
        .padding()
}
