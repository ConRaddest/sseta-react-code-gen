import { FieldErrors } from "react-hook-form"
import { SpSystemUserUpdateRequest } from "@/types/api.types"

interface UseSpSystemUserUpdateFieldsProps {
  errors: FieldErrors<SpSystemUserUpdateRequest>
  disabledFields?: string[]
}

export default function useSpSystemUserUpdateFields(props: UseSpSystemUserUpdateFieldsProps) {
  const { errors, disabledFields = [] } = props

  return {
    mobileNumber: {
      props: {
        id: "system-user-mobileNumber",
        name: "mobileNumber",
        heading: "Mobile Number",
        type: "phone" as const,
        placeholder: "Enter mobile number...",
        disabled: disabledFields.includes("mobileNumber"),
        error: errors.mobileNumber,
      },
      rules: { },
    },
  }
}
